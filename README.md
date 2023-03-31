# OpenTelemetry.Proxy

Generate activity to wrap method, modify inner activity name or discard inner activity.

## Attribute

### [ActivitySource]

#### DynamicProxy
[ActivitySource] can be define on `interface` or `class`, all methods(interface) or virtaul method(class) will automatically generate activity, except define [NonActivity] on method.

`IncludeNonAsyncStateMachineMethod`: default include all async method of interface or define [AsyncStateMachine] public or protected virtual method of class (except `async void` method). If true, will include all method of interface and all public or protected virtual method of class.

#### StaticProxy
[ActivitySource] can be define on `class`, all methods will automatically generate activity, except define [NonActivity] on method.

`IncludeNonAsyncStateMachineMethod`: default include all define [AsyncStateMachine] public method of class (except `async void` method). If true, will include all method of class.

### [Activity]
[Activity] can be define on method only.

### [NonActivity]
If define [NonActivity] on method, call method will not generate activity. if `SuppressInstrumentation` is true, inner activity will be discard.

### [ActivityName]
Modify inner activity DisplayName, must invoke `TracerProviderBuilder.AddProcessor(new ActivityNameProcessor())`. If type define [ActivitySource], method define [Activity] or [NonActivity], [ActivityName] will not take effect.

### [ActivityTag]

> Priority: [NonActivity] > [Activity] > [ActivitySource] > [ActivityName]

## About DynamicProxy and StaticProxy

1. DynamicProxy work on runtime, it will generate a proxy class to wrap method, so it not support AOT.

2. StaticProxy work on compile time, it modify raw type IL code, so it support **AOT**.

3. DynamicProxy will not generate a proxy class if type already processed by StaticProxy.

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
