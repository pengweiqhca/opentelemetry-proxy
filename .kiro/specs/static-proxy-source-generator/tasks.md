# 实现计划：Static Proxy Source Generator

## 概述

将 OpenTelemetry.StaticProxy 从 Metalama ISourceTransformer 迁移到 C# 14 Roslyn Incremental Source Generator + Interceptors。实现分为：项目搭建、属性清理、元数据提取、调用点扫描、代码生成（三种模式）、ActivitySource 管理、泛型支持、诊断报告、测试迁移。

## 任务

- [x] 1. 项目搭建与属性清理
  - [x] 1.1 创建 Source Generator 项目
    - 创建 `src/OpenTelemetry.StaticProxy.SourceGenerator/` 项目目录
    - 创建 `OpenTelemetry.StaticProxy.SourceGenerator.csproj`，目标框架 `netstandard2.0`，设置 `EnforceExtendedAnalyzerRules=true`、`IsRoslynComponent=true`
    - 添加 `Microsoft.CodeAnalysis.CSharp` 5.0.0 包引用（`PrivateAssets="all"`）
    - 配置 NuGet 打包：将输出 DLL 打包到 `analyzers/dotnet/cs` 路径
    - 创建 `ProxySourceGenerator.cs` 入口类，实现 `IIncrementalGenerator` 接口并标注 `[Generator(LanguageNames.CSharp)]`，`Initialize` 方法暂留空
    - 更新 `src/OpenTelemetry.StaticProxy/OpenTelemetry.StaticProxy.csproj`，添加对新 Source Generator 项目的 `ProjectReference`（`PrivateAssets="All"`），并在打包配置中添加新 DLL 的 `analyzers` 路径
    - _需求: 1.1, 1.2, 1.3, 1.4, 1.5_

  - [x] 1.2 从 ActivitySourceAttribute 和 ActivityAttribute 移除 VariableName 属性
    - 从 `src/OpenTelemetry.Proxy/ActivitySourceAttribute.cs` 中删除 `VariableName` 属性及其 XML 注释
    - 从 `src/OpenTelemetry.Proxy/ActivityAttribute.cs` 中删除 `VariableName` 属性及其 XML 注释
    - _需求: 13.1, 13.2, 13.3, 13.4_

  - [x] 1.3 移除 ProxyHasGeneratedAttribute
    - 删除 `src/OpenTelemetry.Proxy/ProxyHasGeneratedAttribute.cs` 文件
    - 从 `src/OpenTelemetry.Proxy/OpenTelemetry.Proxy.csproj` 中移除相关引用（如有）
    - _需求: 9.1, 9.2, 9.3, 9.4_

  - [x] 1.4 复制共享分析逻辑到新项目
    - 将 `src/Shared/ActivityTag.cs` 的 `Compile Include` 添加到新项目
    - 从 SourceTransformer 项目中复制并适配以下可复用文件到新项目：`ITypeContext.cs`、`IMethodContext.cs`、`ActivityContext.cs`、`ActivityNameContext.cs`、`ActivitySourceContext.cs`、`ActivityTagSource.cs`、`NoAttributeTypeContext.cs`、`SyntaxExtensions.cs`、`TypeSyntaxContext.cs`、`PartialVisitor.cs`、`TypeMethods.cs`
    - 移除复制文件中对 Metalama 的所有引用
    - 移除所有与 `VariableName` 相关的代码（`ActivitySourceContext.VariableName`、`ActivityContext.VariableName`、`ActivityContext.ActivitySourceVariableName`、`IActivitySourceContext.VariableName`）
    - _需求: 1.6, 2.1, 2.7, 2.8_

- [x] 2. 检查点 — 确保项目可编译
  - 确保所有项目可编译通过，如有问题请询问用户。

- [x] 3. 元数据提取与不可变模型
  - [x] 3.1 定义不可变元数据模型
    - 创建 `Models/TypeMetadata.cs`，定义 `readonly record struct TypeMetadata`（TypeFullName、ActivitySourceName、Kind、IncludeNonAsyncStateMachineMethod、SuppressInstrumentation、TypeTags、Members、Methods）
    - 创建 `Models/MethodMetadata.cs`，定义 `readonly record struct MethodMetadata`（ContainingTypeFullName、MethodName、MethodSymbolKey、Mode、ActivityName、Kind、SuppressInstrumentation、AdjustStartTime、IsStatic、IsVoid、IsAsync、InTags、OutTags、Parameters、ReturnType）
    - 创建 `Models/TagMetadata.cs`，定义 `readonly record struct TagMetadata`（TagName、SourceName、Source、Expression）
    - 创建 `Models/InterceptCallSite.cs`，定义 `readonly record struct InterceptCallSite`（Target、Location、ResolvedMethod）
    - 创建 `Models/MethodMode.cs`，定义 `enum MethodMode`（Activity、ActivityName、SuppressInstrumentation）
    - 创建 `Models/TagSource.cs`，定义 `enum TagSource`（Parameter、ReturnValue、InstanceFieldOrProperty、StaticFieldOrProperty）
    - 创建 `EquatableArray<T>` 辅助类型，实现 `IEquatable` 以支持 Incremental Generator 缓存
    - 所有模型必须正确实现 `Equals`/`GetHashCode` 以确保增量缓存有效
    - _需求: 1.5, 2.1, 2.2, 2.3, 2.4, 2.5_

  - [x] 3.2 实现 MetadataExtractor
    - 创建 `MetadataExtractor.cs`，复用 `ProxyVisitor` 的特性解析逻辑
    - 实现 `ExtractTypeMetadata(GeneratorAttributeSyntaxContext, CancellationToken)` → `TypeMetadata`：解析 `[ActivitySource]` 的 ActivitySourceName、Kind、IncludeNonAsyncStateMachineMethod、SuppressInstrumentation，收集类型级 `[ActivityTags]`，收集成员（字段/属性）信息和方法列表
    - 实现 `ExtractActivityMethodMetadata(GeneratorAttributeSyntaxContext, CancellationToken)` → `MethodMetadata`：解析 `[Activity]` 的 ActivityName、Kind、SuppressInstrumentation，解析参数和返回值上的 `[ActivityTag]`，解析 `[ActivityTags]` 中的 Tag 映射
    - 实现 `ExtractActivityNameMetadata(GeneratorAttributeSyntaxContext, CancellationToken)` → 类型或方法级元数据：解析 `[ActivityName]` 的 ActivityName、AdjustStartTime
    - 实现 `ExtractNonActivityMetadata(GeneratorAttributeSyntaxContext, CancellationToken)` → `MethodMetadata`：解析 `[NonActivity]` 的 SuppressInstrumentation 参数
    - 实现 Tag 来源解析逻辑：参数 → Parameter、返回值 → ReturnValue、实例字段/属性 → InstanceFieldOrProperty、静态字段/属性 → StaticFieldOrProperty、ref 参数 → 同时 InTag 和 OutTag、out 参数 → 仅 OutTag
    - 实现方法过滤规则：`IncludeNonAsyncStateMachineMethod=false` 时仅检查 `async` 关键字修饰符；接口类型中所有方法均包含
    - _需求: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 8.1, 8.2, 8.3, 8.4_

  - [x] 3.3 编写元数据提取的属性测试
    - **Property 1: 特性元数据提取正确性**
    - 使用 FsCheck.Xunit 生成随机特性配置组合，通过 CSharpGeneratorDriver 运行 Source Generator，验证提取的元数据值与特性参数一致
    - **验证: 需求 2.1, 2.2, 2.3, 2.4, 2.5**

  - [x] 3.4 编写方法过滤规则的属性测试
    - **Property 10: 方法过滤规则正确性**
    - 生成随机方法可见性（public/private/internal）和 async/非 async 组合，验证 IncludeNonAsyncStateMachineMethod 标志的过滤行为
    - **验证: 需求 8.1, 8.2, 8.3**

- [x] 4. IIncrementalGenerator 管线与调用点扫描
  - [x] 4.1 实现 ProxySourceGenerator 管线注册
    - 在 `ProxySourceGenerator.Initialize` 中注册四个 `ForAttributeWithMetadataName` Provider：
      - `OpenTelemetry.Proxy.ActivitySourceAttribute` → `ExtractTypeMetadata`
      - `OpenTelemetry.Proxy.ActivityAttribute` → `ExtractActivityMethodMetadata`
      - `OpenTelemetry.Proxy.ActivityNameAttribute` → `ExtractActivityNameMetadata`
      - `OpenTelemetry.Proxy.NonActivityAttribute` → `ExtractNonActivityMetadata`
    - 使用 `.Collect()` 和 `.Combine()` 合并所有元数据
    - 与 `context.CompilationProvider` 组合后注册 `RegisterSourceOutput`
    - 在 `Execute` 回调中调用 CallSiteScanner 和 InterceptorEmitter
    - _需求: 1.1, 1.5, 2.1, 2.2, 2.3, 2.6_

  - [x] 4.2 实现 CallSiteScanner
    - 创建 `CallSiteScanner.cs`
    - 实现 `ScanCallSites(Compilation, ImmutableArray<MethodMetadata>, CancellationToken)` → `ImmutableArray<InterceptCallSite>`
    - 遍历所有语法树中的 `InvocationExpressionSyntax`
    - 通过 `SemanticModel.GetSymbolInfo()` 匹配目标方法（使用 MethodSymbolKey 或 IMethodSymbol 比较）
    - 使用 `SemanticModel.GetInterceptableLocation()` 获取拦截位置
    - 处理隐式 ActivitySource 场景：方法标注 `[Activity]` 但类型未标注 `[ActivitySource]` 时，自动创建对应的 ActivitySource 元数据
    - 支持接口方法和抽象方法的调用点匹配
    - _需求: 3.1, 4.1, 5.1, 6.5, 8.4, 8.5, 8.6_

- [x] 5. 检查点 — 确保管线可运行
  - 确保所有测试通过，如有问题请询问用户。

- [x] 6. Interceptor 代码生成 — Activity 模式
  - [x] 6.1 实现 Activity 模式的 InterceptorEmitter
    - 创建 `InterceptorEmitter.cs`（或在已有文件中添加）
    - 实现 `EmitActivityInterceptor(InterceptCallSite, StringBuilder)` 方法
    - 生成 `[InterceptsLocation]` 特性标注
    - 生成拦截方法签名：实例方法 → `static` 扩展方法（`this T @this`），静态方法 → `static` 方法
    - 生成 `ActivitySource.StartActivity()` 调用，传入正确的 ActivityName 和 Kind
    - 生成 InTag 的 `activity.SetTag()` 调用（在 `if (activity != null)` 块中）
    - 生成 `try-catch-finally` 块：catch 中调用 `ActivityExtensions.SetExceptionStatus()` 并重新抛出，finally 中调用 `activity?.Dispose()`
    - 生成 OutTag 的 `activity.SetTag()` 调用（在方法返回前，try 块内）
    - 处理 void、同步返回值、Task、ValueTask、FSharpAsync 和自定义 Awaitable 返回类型
    - _需求: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.8, 3.9, 3.10_

  - [x] 6.2 实现 SuppressInstrumentation 条件生成
    - 当 `SuppressInstrumentation = true` 时，在 Activity 模式拦截方法中生成 `SuppressInstrumentationScope.Begin()` 调用
    - 在 finally 块中生成对应的 `Dispose()` 调用
    - 当 `SuppressInstrumentation = false` 时，不生成这些调用
    - _需求: 3.7_

  - [x] 6.3 编写 Activity 模式代码结构的属性测试
    - **Property 2: Activity 模式拦截代码结构完整性**
    - 生成随机方法签名（不同参数类型、返回类型），通过 CSharpGeneratorDriver 运行 Source Generator，验证生成代码包含 StartActivity、try-catch-finally、SetExceptionStatus、Dispose
    - **验证: 需求 3.1, 3.2, 3.4, 3.5, 3.6, 3.10**

  - [x] 6.4 编写 Tag 设置代码生成的属性测试
    - **Property 3: Tag 设置代码生成正确性**
    - 生成随机 Tag 配置（InTags、OutTags），验证生成代码中 SetTag 调用的数量和参数正确
    - **验证: 需求 3.3, 3.8**

  - [x] 6.5 编写 SuppressInstrumentation 条件生成的属性测试
    - **Property 4: SuppressInstrumentation 条件生成**
    - 随机 SuppressInstrumentation 标志，验证条件代码的有无
    - **验证: 需求 3.7**

- [x] 7. Interceptor 代码生成 — ActivityName 与 SuppressInstrumentation 模式
  - [x] 7.1 实现 ActivityName 模式的 InterceptorEmitter
    - 实现 `EmitActivityNameInterceptor(InterceptCallSite, StringBuilder)` 方法
    - 生成 `using(InnerActivityAccessor.SetActivityContext(...))` 包装
    - 在 `InnerActivityContext` 中设置 Name、AdjustStartTime 属性
    - 当存在 InTags 时，构造 `Dictionary<string, object?>` 并填充 Tag 值
    - _需求: 4.1, 4.2, 4.3, 4.4_

  - [x] 7.2 实现 SuppressInstrumentation 模式的 InterceptorEmitter
    - 实现 `EmitSuppressInstrumentationInterceptor(InterceptCallSite, StringBuilder)` 方法
    - 生成 `using(SuppressInstrumentationScope.Begin())` 包装原方法调用
    - _需求: 5.1_

  - [x] 7.3 编写 ActivityName 模式代码结构的属性测试
    - **Property 5: ActivityName 模式拦截代码结构完整性**
    - 生成随机 ActivityName 配置，验证生成代码包含 SetActivityContext、正确的 Name 和 AdjustStartTime
    - **验证: 需求 4.1, 4.2, 4.3, 4.4**

  - [x] 7.4 编写 SuppressInstrumentation 模式代码结构的属性测试
    - **Property 6: SuppressInstrumentation 模式拦截代码结构**
    - 验证 `[NonActivity(true)]` 方法生成 `SuppressInstrumentationScope.Begin()` 包装
    - **验证: 需求 5.1**

- [x] 8. ActivitySource 管理与 Tag 表达式
  - [x] 8.1 实现 ActivitySourceHolder 生成
    - 创建 `ActivitySourceHolderEmitter.cs`（或在 InterceptorEmitter 中实现）
    - 在生成文件中创建 `internal static class ActivitySourceHolder`
    - 为每个需要 ActivitySource 的类型生成 `static readonly ActivitySource` 字段
    - 使用 ActivitySourceName 和 `typeof(T).Assembly.GetName().Version?.ToString()` 作为构造参数
    - 当 ActivitySourceName 未指定时，使用类型完全限定名
    - 按 ActivitySourceName 去重，相同名称只生成一个字段
    - _需求: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7_

  - [x] 8.2 实现 Tag 值表达式生成
    - 在 InterceptorEmitter 中实现 Tag 值表达式解析：
      - 参数 → `paramName`
      - 实例字段/属性 → `@this.MemberName`
      - 静态字段/属性 → `TypeName.MemberName`
      - 返回值 → 捕获到局部变量
      - 带 Expression → `source + expression[1..]`（如 `param.Property`）
    - _需求: 7.1, 7.2, 7.3, 7.4, 7.5_

  - [x] 8.3 编写 ActivitySource 生成的属性测试
    - **Property 7: ActivitySource 字段生成正确性**
    - 生成随机类型配置，验证 ActivitySource 字段的构造参数正确
    - **验证: 需求 6.2, 6.3, 6.5**

  - [x] 8.4 编写 ActivitySource 去重的属性测试
    - **Property 8: ActivitySource 去重**
    - 生成具有重复 ActivitySourceName 的类型，验证只生成一个 ActivitySource 字段
    - **验证: 需求 6.6**

  - [x] 8.5 编写 Tag 来源解析的属性测试
    - **Property 9: Tag 来源解析正确性**
    - 生成随机 Tag 来源配置，验证表达式生成正确
    - **验证: 需求 7.1, 7.2, 7.3, 7.4, 7.5**

- [x] 9. 检查点 — 确保三种模式代码生成正确
  - 确保所有测试通过，如有问题请询问用户。

- [x] 10. 泛型支持与诊断报告
  - [x] 10.1 实现泛型类型与泛型方法支持
    - 泛型类型的 ActivitySource 名称使用开放泛型类型名格式（如 `MyClass`1`）
    - 泛型方法的拦截方法保留所有泛型类型参数（包含类型的泛型参数 + 方法自身的泛型参数）
    - 从原方法复制泛型类型约束
    - 确保 `GetInterceptableLocation` 正确处理泛型调用
    - _需求: 10.1, 10.2_

  - [x] 10.2 实现 DiagnosticReporter
    - 创建 `DiagnosticDescriptors.cs`，定义 OTSP001、OTSP002、OTSP003 诊断描述符（复用现有 ID 和消息格式）
    - 在 MetadataExtractor 中集成诊断报告：特性参数表达式无法识别 → OTSP001，参数值为 null → OTSP002，参数类型不匹配 → OTSP003
    - 遇到诊断错误时跳过该方法/类型的代码生成，继续处理其他方法/类型
    - _需求: 11.1, 11.2, 11.3_

  - [x] 10.3 编写泛型支持的属性测试
    - **Property 11: 泛型类型与方法支持**
    - 生成随机泛型参数数量，验证 ActivitySource 名称和拦截方法的泛型参数正确
    - **验证: 需求 10.1, 10.2**

- [x] 11. 测试迁移与集成测试
  - [x] 11.1 创建 Source Generator 测试项目
    - 创建 `test/OpenTelemetry.StaticProxy.SourceGenerator.Tests/` 项目
    - 添加 `Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing.XUnit`（或直接使用 `CSharpGeneratorDriver`）和 `FsCheck.Xunit` 包引用
    - 添加对新 Source Generator 项目的 `ProjectReference`
    - 配置 `InternalsVisibleTo` 以便测试可访问内部类型
    - _需求: 12.4_

  - [x] 11.2 迁移功能集成测试
    - 从 `test/OpenTelemetry.StaticProxy.Tests/FunctionTest.cs` 迁移以下测试场景（适配为 Source Generator + Interceptor 方式）：
      - SuppressInstrumentationScope 测试
      - ActivityName 测试
      - Activity 创建和 Tag 设置测试
      - 异常状态设置测试
      - OutTag 和 ReturnValue 测试
    - 移除 `ProxyHasGeneratedAttributeTest` 和 `VariableNameTest`（已不适用）
    - 新增接口方法拦截测试
    - 新增抽象方法拦截测试
    - _需求: 8.5, 8.6, 12.2, 12.4_

  - [x] 11.3 编写诊断测试
    - 测试 OTSP001：无法识别的特性参数表达式
    - 测试 OTSP002：null 参数值
    - 测试 OTSP003：类型不匹配
    - _需求: 11.1, 11.2, 11.3_

  - [x] 11.4 编写边界情况测试
    - expression body 方法的拦截
    - throw 表达式的处理
    - 空 Tag 列表
    - 多个 partial 声明的合并
    - _需求: 14.1, 14.2, 14.3, 2.8_

- [x] 12. 整合与清理
  - [x] 12.1 整合生成文件输出
    - 确保每个编译单元生成一个文件，包含 `// <auto-generated/>` 和 `#nullable enable` 头部
    - `ActivitySourceHolder` 使用 `internal static class`
    - `Interceptors` 使用 `file static class`（file-scoped）
    - 确保生成代码中无对 Metalama 的任何引用
    - _需求: 1.6, 6.1, 6.7_

  - [x] 12.2 删除旧的 Metalama SourceTransformer 项目
    - 删除 `src/OpenTelemetry.StaticProxy.SourceTransformer/` 整个目录
    - 删除 `src/OpenTelemetry.StaticProxy.SourceTransformer.Roslyn40/` 整个目录
    - 从解决方案文件（`.sln`）中移除这两个项目的引用
    - 从 `src/OpenTelemetry.StaticProxy/OpenTelemetry.StaticProxy.csproj` 中移除对这两个项目的 `ProjectReference`
    - _需求: 1.6_

  - [x] 12.3 更新 NuGet 打包配置
    - 更新 `src/OpenTelemetry.StaticProxy/OpenTelemetry.StaticProxy.csproj`：
      - 移除对 Metalama SourceTransformer 项目的引用和打包路径
      - 移除对 Roslyn40 项目的引用和打包路径
      - 添加新 Source Generator DLL 的 `analyzers/dotnet/cs` 打包路径
      - 更新 `Description` 移除 Metalama 相关描述
      - 更新 `PackageTags` 移除 Metalama 标签
    - 确保消费方项目引用 NuGet 包后无需额外配置即可使用
    - _需求: 1.3, 1.4, 1.6, 12.1, 12.3_

- [x] 13. 最终检查点 — 确保所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

## 备注

- 标记 `*` 的任务为可选任务，可跳过以加速 MVP 开发
- 每个任务引用了具体的需求编号以确保可追溯性
- 检查点任务确保增量验证
- 属性测试验证通用正确性属性，单元测试验证具体示例和边界情况
