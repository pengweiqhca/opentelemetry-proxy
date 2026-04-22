# OpenTelemetry.Proxy

Generate OpenTelemetry Activity instrumentation at compile time using Roslyn Source Generator + C# 14 Interceptors.

## How it works

The Source Generator scans your code for attribute annotations (`[ActivitySource]`, `[Activity]`, etc.) and generates interceptor methods that wrap your method calls with OpenTelemetry instrumentation — Activity creation, tag setting, exception handling, and resource cleanup. All at compile time, zero runtime overhead.

## Attributes

### [ActivitySource]

Mark a type to automatically generate Activity for its methods.

`IncludeNonAsyncStateMachineMethod`: By default, only `async` methods are auto-included. Set to `true` to include all public methods.

### [Activity]

Mark a method to generate Activity wrapping. Can specify `ActivityName`, `Kind`, and `SuppressInstrumentation`.

### [NonActivity]

Exclude a method from Activity generation. If `SuppressInstrumentation` is `true`, downstream instrumentation will be suppressed.

### [ActivityName]

Modify the DisplayName of an inner activity. Requires `TracerProviderBuilder.AddActivityNameProcessor()`.

> Priority: [NonActivity] > [Activity] > [ActivityName] (method) > [ActivitySource] > [ActivityName] (class)

### [ActivityTag]

Add a tag to the Activity. Can be defined on parameters or return values.

### [ActivityTags]

Add multiple tags to the Activity. Can be defined on types or methods. Tags are resolved against method parameters, instance fields/properties, and static fields/properties.

## Setup

### 1. Install the NuGet package

```
dotnet add package PW.OpenTelemetry.Proxy
```

### 2. Register ActivitySources

The Source Generator creates an `ActivitySourceHolder` class with an extension method to register all generated ActivitySources:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(b => b
        .AddMyAppSources()           // Generated: registers all ActivitySources in this assembly
        .AddActivityNameProcessor()   // Required for [ActivityName] support
        .AddOtlpExporter());
```

The method name is `Add{AssemblyName}Sources()`, unique per assembly.

### 3. Disable generation (optional)

```xml
<PropertyGroup>
  <DisableProxyGenerator>true</DisableProxyGenerator>
</PropertyGroup>
```

## Tag expression

Must start with `$` on `[ActivityTag]`.

Any valid C# member access expression. For example:
- `[ActivityTag(Expression = "$.Length")]` on a `string` parameter generates `param.Length`
- `[ActivityTag(Expression = "$.Hour")]` on a `DateTime` return value generates `@return.Hour`

## Example

```csharp
[ActivitySource]
public class OrderService
{
    [Activity]
    public async Task<Order> CreateOrder([ActivityTag] int customerId, [ActivityTag] string product)
    {
        // Activity is automatically created with tags "customerId" and "product"
        return await SaveOrder(customerId, product);
    }

    [ActivityName(AdjustStartTime = true)]
    public async Task ProcessPayment([ActivityTag] decimal amount)
    {
        // Inner activity name is set to "OrderService.ProcessPayment"
        await ChargeCard(amount);
    }

    [NonActivity(true)]
    public void InternalCleanup()
    {
        // Downstream instrumentation is suppressed
    }
}
```

## QA

### How to get ActivitySource name?

```csharp
ActivitySourceAttribute.GetActivitySourceName(typeof(YourType))
```
