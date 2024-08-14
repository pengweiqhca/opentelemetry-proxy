# OpenTelemetry.Proxy

Generate an activity to wrap the method, modify name of the inner activity or discard inner activity.

## Attribute

### [ActivitySource]

#### DynamicProxy
[ActivitySource] all methods defined in the interface or virtual methods defined in the class will automatically generate activity, unless defined [NonActivity] on method.

`IncludeNonAsyncStateMachineMethod`: default include all async methods of defined in an interface, or all [AsyncStateMachine] public or protected virtual method of class (except for `async void` method). If true, all methods defined in the interface will be included and all public or protected virtual methods of the class.

#### StaticProxy
[ActivitySource] all methods will automatically generate activity, except for those that are defined [NonActivity] on method.

`IncludeNonAsyncStateMachineMethod`: public methods defined in the class that are marked with the [AsyncStateMachine] attribute (except for `async void` method). If true, will include all methods of class.

> Needs to be installed in each working project, and install `Metalama.Compiler` or `Fody` (`Fody` need some additional file), recommend  `Metalama.Compiler`.

### [Activity]
[Activity] can be defined on method only.

### [NonActivity]
If defined [NonActivity] on method, call method will not generate an activity. if `SuppressInstrumentation` is true, the inner activity will be discarded.

### [ActivityName]
To modify the DisplayName of an inner activity, you must invoke `TracerProviderBuilder.AddActivityNameProcessor()`. If a type is defined with the [ActivitySource] attribute or a method is defined with the [Activity] or [NonActivity] attribute, the [ActivityName] attribute will not take effect.

> Priority: [NonActivity] > [Activity] > [ActivityName] (method) > [ActivitySource] > [ActivityName] (class)

### [ActivityTag]

To add tag to activity, it defined on parameter or return value.

### [ActivityTags]

To add tags to activity, it defined on type or method.

## About DynamicProxy and StaticProxy

|                  | DynamicProxy                | StaticProxy / Fody        | StaticProxy / Metalama                        |
| ---------------- | --------------------------- | ------------------------- | --------------------------------------------- |
| AOT              | ❌                           | ✔️                         | ✔️                                             |
| Work in          | Runtime                     | Compile time (IL rewrite) | Compile time (Syntax tree)                    |
| Possible problem | No                          | Runtime error.            | Compile error.                                |
| Support scenario | interface or virtual method | Any method with a body.   | Any method with a body except local function. |
| Work order (ASC) | 3                           | 2                         | 1                                             |
| Performance      | ⭐⭐⭐                         | ⭐⭐⭐⭐⭐                     | ⭐⭐⭐⭐⭐                                         |
| Limit            |                             | Not support `#line`       |                                               |

## QA
### How to get ActivitySource name?
`ActivitySourceAttribute.GetActivitySourceName(typeof(YourType))`

See [demo](https://github.com/pengweiqhca/opentelemetry-proxy/blob/main/demo/OpenTelemetry.Proxy.Demo/Program.cs#L23)

### How to generate proxy class?
``` C#
ProxyGenerator generator = ...
IActivityInvokerFactory invokerFactory = ...

var proxyType = generator.CreateClassProxy<YourType>(new ActivityInterceptor(invokerFactory));
```
See [demo](https://github.com/pengweiqhca/opentelemetry-proxy/blob/main/demo/OpenTelemetry.DynamicProxy.Demo/ServiceCollectionExtensions.cs#L13).
