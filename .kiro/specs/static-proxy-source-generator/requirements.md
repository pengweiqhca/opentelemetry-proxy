# 需求文档

## 简介

将 OpenTelemetry.Proxy 从基于 Metalama 的静态代理织入重构为基于 C# 14 Source Generator + Interceptors 的实现。当前实现依赖 Metalama.Compiler.Sdk 的 ISourceTransformer 在编译期重写语法树，注入 Activity 创建、Tag 设置、异常处理和资源释放代码。新实现将使用 Roslyn Incremental Source Generator 分析用户代码中的特性标注，并通过 C# 14 Interceptors 机制在编译期拦截目标方法调用，生成包含 OpenTelemetry 插桩逻辑的拦截方法，从而完全移除对 Metalama 和 DynamicProxy 的依赖。

## 术语表

- **Source_Generator**: Roslyn Incremental Source Generator（IIncrementalGenerator），在编译期分析语法和语义模型并生成额外的 C# 源代码文件
- **Interceptor**: C# 14 编译器特性，允许在编译期将对某个方法的调用重定向到另一个拦截方法，通过 [InterceptsLocation] 特性指定被拦截的调用位置
- **Metalama**: 当前使用的编译期代码织入框架，提供 ISourceTransformer 接口用于重写语法树
- **Activity**: System.Diagnostics.Activity，OpenTelemetry 中表示一个分布式追踪 Span 的对象
- **ActivitySource**: System.Diagnostics.ActivitySource，用于创建 Activity 实例的工厂对象
- **Attribute_System**: 项目中定义的一组特性（[ActivitySource]、[Activity]、[ActivityName]、[ActivityTag]、[ActivityTags]、[NonActivity]），用于声明式标注需要插桩的类型和方法
- **InTags**: 方法入口处设置的 Activity Tag，来源于方法参数、字段或属性
- **OutTags**: 方法返回时设置的 Activity Tag，来源于返回值、ref/out 参数
- **SuppressInstrumentation**: 抑制下游插桩的机制，通过 OpenTelemetry.SuppressInstrumentationScope.Begin() 实现
- **InnerActivityContext**: 用于 [ActivityName] 模式的内部上下文对象，通过 InnerActivityAccessor.SetActivityContext() 设置

## 需求

### 需求 1：Source Generator 基础架构

**用户故事：** 作为库的维护者，我希望用 Roslyn Incremental Source Generator 替换 Metalama ISourceTransformer，以便移除对 Metalama 的依赖并使用标准的 .NET 编译器扩展机制。

#### 验收标准

1. THE Source_Generator SHALL 实现 IIncrementalGenerator 接口，并通过 [Generator] 特性注册
2. THE Source_Generator SHALL 以 netstandard2.0 为目标框架，确保与 .NET Framework 和 .NET Core 工具链兼容
3. THE Source_Generator SHALL 作为 Analyzer 引用方式打包到 NuGet 包中，替换当前的 Metalama Transformer 打包方式
4. WHEN 项目引用新的 NuGet 包时，THE Source_Generator SHALL 在编译期自动运行，无需用户额外配置
5. THE Source_Generator SHALL 使用 Incremental Generator 的 RegisterSourceOutput 管线，确保增量编译性能
6. WHEN 编译完成后，THE Source_Generator SHALL 不在最终程序集中引入任何对 Metalama 的依赖

### 需求 2：特性分析与语义识别

**用户故事：** 作为库的使用者，我希望现有的特性标注（[ActivitySource]、[Activity]、[ActivityName]、[ActivityTag]、[ActivityTags]、[NonActivity]）在新实现中保持完全兼容，以便无需修改业务代码即可迁移。

#### 验收标准

1. WHEN 类型标注了 [ActivitySource] 特性时，THE Source_Generator SHALL 识别该类型并提取 ActivitySourceName、Kind、IncludeAllMethods 和 SuppressInstrumentation 配置
2. WHEN 方法标注了 [Activity] 特性时，THE Source_Generator SHALL 识别该方法并提取 ActivityName、Kind 和 SuppressInstrumentation 配置
3. WHEN 类型或方法标注了 [ActivityName] 特性时，THE Source_Generator SHALL 识别并提取 ActivityName 和 AdjustStartTime 配置
4. WHEN 参数或返回值标注了 [ActivityTag] 特性时，THE Source_Generator SHALL 识别并提取 Name 和 Expression 配置
5. WHEN 类型或方法标注了 [ActivityTags] 特性时，THE Source_Generator SHALL 解析 Tags 数组并将每个 Tag 映射到对应的参数、字段或属性
6. WHEN 方法标注了 [NonActivity] 特性时，THE Source_Generator SHALL 根据 SuppressInstrumentation 参数决定是跳过该方法还是仅包装 SuppressInstrumentationScope
7. THE Source_Generator SHALL 支持 class、struct、record 和 interface 类型声明上的特性分析
8. THE Source_Generator SHALL 支持 partial 类型，正确合并来自多个 partial 声明的特性和成员信息

### 需求 3：Interceptor 代码生成 — Activity 模式

**用户故事：** 作为库的使用者，我希望标注了 [Activity] 的方法在编译期自动生成包含 Activity 创建、Tag 设置、异常处理和资源释放的拦截代码，以便获得与 Metalama 实现相同的运行时行为。

#### 验收标准

1. WHEN 方法标注了 [Activity] 特性时，THE Source_Generator SHALL 生成一个拦截方法，该方法通过 [InterceptsLocation] 特性拦截对原方法的调用
2. THE Source_Generator SHALL 在拦截方法中声明一个局部变量，通过 ActivitySource.StartActivity() 创建 Activity 实例
3. WHEN Activity 实例不为 null 且存在 InTags 时，THE Source_Generator SHALL 在方法入口处调用 Activity.SetTag() 设置所有入口 Tag
4. THE Source_Generator SHALL 将原方法调用包装在 try-catch-finally 块中
5. WHEN 方法执行抛出异常时，THE Source_Generator SHALL 在 catch 块中调用 ActivityExtensions.SetExceptionStatus() 设置错误状态，并重新抛出异常
6. THE Source_Generator SHALL 在 finally 块中调用 Activity.Dispose() 释放 Activity 实例
7. WHEN [Activity] 特性的 SuppressInstrumentation 为 true 时，THE Source_Generator SHALL 在拦截方法中调用 SuppressInstrumentationScope.Begin()，并在 finally 块中释放该 Scope
8. WHEN 方法存在 OutTags（返回值或 ref/out 参数上的 [ActivityTag]）时，THE Source_Generator SHALL 在方法返回前将返回值或 ref/out 参数的值设置为 Activity Tag
9. THE Source_Generator SHALL 在拦截方法中正确处理 void、同步返回值、Task、ValueTask、FSharpAsync 和自定义 Awaitable 返回类型
10. WHEN [Activity] 特性指定了 Kind 参数时，THE Source_Generator SHALL 将该 Kind 传递给 ActivitySource.StartActivity() 调用

### 需求 4：Interceptor 代码生成 — ActivityName 模式

**用户故事：** 作为库的使用者，我希望标注了 [ActivityName] 的方法在编译期自动生成通过 InnerActivityAccessor 设置活动上下文的拦截代码，以便支持内部活动命名场景。

#### 验收标准

1. WHEN 方法标注了 [ActivityName] 特性或继承自标注了 [ActivityName] 的类型时，THE Source_Generator SHALL 生成一个拦截方法，通过 InnerActivityAccessor.SetActivityContext() 包装原方法调用
2. THE Source_Generator SHALL 在 InnerActivityContext 中设置 Name、AdjustStartTime 和 Tags 属性
3. WHEN 方法存在 InTags 时，THE Source_Generator SHALL 构造 Dictionary<string, object?> 并填充所有入口 Tag 值
4. THE Source_Generator SHALL 使用 using 语句包装 SetActivityContext() 调用，确保上下文在方法完成后正确清理

### 需求 5：Interceptor 代码生成 — SuppressInstrumentation 模式

**用户故事：** 作为库的使用者，我希望标注了 [NonActivity(true)] 的方法在编译期自动生成 SuppressInstrumentationScope 包装代码，以便在方法执行期间抑制下游插桩。

#### 验收标准

1. WHEN 方法标注了 [NonActivity(true)]（SuppressInstrumentation = true）时，THE Source_Generator SHALL 生成一个拦截方法，使用 using(SuppressInstrumentationScope.Begin()) 包装原方法调用
2. WHEN 方法标注了 [NonActivity] 且 SuppressInstrumentation 为 false 或未指定时，THE Source_Generator SHALL 跳过该方法，不生成任何拦截代码

### 需求 6：ActivitySource 实例管理

**用户故事：** 作为库的使用者，我希望 Source Generator 在调用方的生成代码中集中管理 ActivitySource 实例，以便拦截方法可以使用它们创建 Activity，且不要求被标注的类型声明为 partial。

#### 验收标准

1. THE Source_Generator SHALL 在生成的拦截代码文件中创建内部静态类，集中管理所有 ActivitySource 实例
2. WHEN 类型标注了 [ActivitySource] 特性时，THE Source_Generator SHALL 在该静态类中生成一个 static readonly ActivitySource 字段
3. THE Source_Generator SHALL 使用 ActivitySourceName 和 typeof(T).Assembly.GetName().Version?.ToString() 作为 ActivitySource 构造函数参数
4. WHEN ActivitySourceName 未指定时，THE Source_Generator SHALL 使用类型的完全限定名作为 ActivitySource 名称
5. WHEN 方法标注了 [Activity] 但所在类型未标注 [ActivitySource] 时，THE Source_Generator SHALL 隐式生成对应的 ActivitySource 实例（使用类型完全限定名）
6. THE Source_Generator SHALL 按 ActivitySourceName 去重，相同名称的 ActivitySource 在同一调用方程序集中只生成一个实例
7. THE Source_Generator SHALL 不要求被标注的类型声明为 partial

### 需求 7：Tag 来源解析

**用户故事：** 作为库的使用者，我希望 Tag 值能从方法参数、返回值、实例字段/属性和静态字段/属性中正确解析，以便 Activity Tag 能准确反映运行时数据。

#### 验收标准

1. WHEN [ActivityTag] 标注在方法参数上时，THE Source_Generator SHALL 将该参数值作为 InTag 来源
2. WHEN [ActivityTag] 标注在返回值上（[return: ActivityTag]）时，THE Source_Generator SHALL 将返回值作为 OutTag 来源
3. WHEN [ActivityTags] 中的 Tag 名称匹配实例字段或属性名时，THE Source_Generator SHALL 使用 this.FieldOrPropertyName 作为 Tag 值表达式
4. WHEN [ActivityTags] 中的 Tag 名称匹配静态字段或属性名时，THE Source_Generator SHALL 使用 TypeName.FieldOrPropertyName 作为 Tag 值表达式
5. WHEN [ActivityTag] 指定了 Expression 属性时，THE Source_Generator SHALL 将表达式附加到 Tag 来源值上（例如 parameter.Property）
6. WHEN 参数为 ref 类型时，THE Source_Generator SHALL 同时将其作为 InTag 和 OutTag 来源
7. WHEN 参数为 out 类型时，THE Source_Generator SHALL 仅将其作为 OutTag 来源

### 需求 8：方法可见性与自动包含规则

**用户故事：** 作为库的使用者，我希望 Source Generator 在保持原有方法选择规则的基础上，新增对接口方法和抽象方法的支持（Metalama 因重写方法体的限制无法支持），以便获得更完整的插桩覆盖。

#### 验收标准

1. WHEN 类型标注了 [ActivitySource] 且 IncludeAllMethods 为 false 时，THE Source_Generator SHALL 仅对 async 方法和显式标注了 [Activity] 的方法生成拦截代码
2. WHEN 类型标注了 [ActivitySource] 且 IncludeAllMethods 为 true 时，THE Source_Generator SHALL 对所有 public 方法生成拦截代码
3. WHEN 方法未标注 [Activity] 或 [ActivityName] 且不是 public 方法时，THE Source_Generator SHALL 跳过该方法
4. WHEN 类型为 interface 时，THE Source_Generator SHALL 对所有方法生成拦截代码（无论可见性）
5. WHEN 接口方法标注了 [Activity] 或所在接口标注了 [ActivitySource] 时，THE Source_Generator SHALL 在调用点生成拦截代码，包装对接口方法的调用
6. WHEN 抽象方法标注了 [Activity] 或所在抽象类标注了 [ActivitySource] 时，THE Source_Generator SHALL 在调用点生成拦截代码，包装对抽象方法的调用

### 需求 9：移除 ProxyHasGeneratedAttribute

**用户故事：** 作为库的维护者，我希望移除 ProxyHasGeneratedAttribute 标记特性，因为 Metalama 在被调用库的程序集中织入代码需要此标记来检测是否已生成，而 Source Generator + Interceptors 在调用方生成拦截代码，被调用库本身不会被修改，该标记已无意义。

#### 验收标准

1. THE Source_Generator SHALL 不再生成类型级别的 [ProxyHasGenerated] 特性
2. THE Source_Generator SHALL 不再生成程序集级别的 [assembly: ProxyHasGenerated] 特性
3. THE ProxyHasGeneratedAttribute 类 SHALL 从 OpenTelemetry.Proxy 库中移除
4. THE Source_Generator SHALL 移除所有与 ProxyHasGeneratedAttribute 相关的生成逻辑

### 需求 10：泛型类型与泛型方法支持

**用户故事：** 作为库的使用者，我希望 Source Generator 正确处理泛型类型和泛型方法上的特性标注，以便泛型场景下的插桩行为正确。

#### 验收标准

1. WHEN 泛型类型标注了 [ActivitySource] 时，THE Source_Generator SHALL 正确生成 ActivitySource 字段，使用开放泛型类型名（如 KeepLineNumberTestClass`1）作为 ActivitySource 名称
2. WHEN 泛型方法标注了 [Activity] 时，THE Source_Generator SHALL 生成正确的拦截方法，保留泛型类型参数

### 需求 11：诊断信息报告

**用户故事：** 作为库的使用者，我希望在特性参数配置错误时收到清晰的编译器诊断信息，以便快速定位和修复问题。

#### 验收标准

1. WHEN 特性参数表达式无法识别时，THE Source_Generator SHALL 报告 OTSP001 错误诊断，包含无法识别的表达式内容
2. WHEN 特性参数值为 null 但期望非 null 时，THE Source_Generator SHALL 报告 OTSP002 错误诊断
3. WHEN 特性参数类型与期望类型不匹配时，THE Source_Generator SHALL 报告 OTSP003 错误诊断，包含期望类型和实际类型

### 需求 12：向后兼容性与迁移

**用户故事：** 作为库的使用者，我希望从 Metalama 版本迁移到 Source Generator 版本时无需修改业务代码，以便迁移过程平滑无痛。

#### 验收标准

1. THE Source_Generator SHALL 保持与 OpenTelemetry.Proxy 库中定义的所有公共特性的完全兼容
2. THE Source_Generator SHALL 生成与 Metalama 实现在运行时行为上等价的代码，包括 Activity 创建、Tag 设置、异常处理和资源释放
3. WHEN 用户将 NuGet 包从 Metalama 版本升级到 Source Generator 版本时，THE Source_Generator SHALL 仅需更换包引用，无需修改任何业务代码
4. THE Source_Generator SHALL 保持现有测试用例（FunctionTest）中验证的所有行为，包括 SuppressInstrumentationScope、ActivityName、Activity 创建、异常状态设置、OutTag 和 ReturnValue 场景（ProxyHasGeneratedAttribute 相关测试除外）

### 需求 13：移除 VariableName 配置属性

**用户故事：** 作为库的维护者，我希望移除 ActivitySourceAttribute.VariableName 和 ActivityAttribute.VariableName 属性，因为在 Source Generator + Interceptors 模式下，生成的字段名和局部变量名完全由生成器内部控制，不再需要用户配置。

#### 验收标准

1. THE Source_Generator SHALL 从 ActivitySourceAttribute 中移除 VariableName 属性
2. THE Source_Generator SHALL 从 ActivityAttribute 中移除 VariableName 属性
3. THE Source_Generator SHALL 使用内部固定的字段名生成 ActivitySource 字段，无需外部配置
4. THE Source_Generator SHALL 使用内部固定的局部变量名生成 Activity 实例，无需外部配置
5. WHEN 用户代码中仍使用了 VariableName 参数时，编译器 SHALL 报告编译错误（因属性已移除），引导用户移除该参数

### 需求 14：Expression Body 与多种方法体形式支持

**用户故事：** 作为库的使用者，我希望 Source Generator 正确处理 expression body 方法（=>）和 block body 方法，以便所有方法体形式都能被正确拦截。

#### 验收标准

1. WHEN 目标方法使用 expression body（=>）语法时，THE Source_Generator SHALL 正确拦截该方法调用并生成等价的插桩代码
2. WHEN 目标方法使用 block body（{...}）语法时，THE Source_Generator SHALL 正确拦截该方法调用并生成等价的插桩代码
3. WHEN expression body 方法包含 throw 表达式时，THE Source_Generator SHALL 正确处理该场景，生成包含 throw 语句的拦截代码
