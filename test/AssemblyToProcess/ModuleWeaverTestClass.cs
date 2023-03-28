using Microsoft.FSharp.Control;
using OpenTelemetry;
using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.Tests.Common;
using System.Reflection;

namespace AssemblyToProcess;

[ActivitySource("ModuleWeaverTestClass", IncludeNonAsyncStateMachineMethod = true)]
public static class ModuleWeaverTestClass
{
    [NonActivity(true)]
    public static bool SuppressInstrumentationScope() => Sdk.SuppressInstrumentation;

    [NonActivity(true)]
    public static async Task<bool> SuppressInstrumentationScopeAsync()
    {
        await Task.Delay(100).ConfigureAwait(false);

        return Sdk.SuppressInstrumentation;
    }

    [NonActivity(true)]
    public static async ValueTask<bool> SuppressInstrumentationScope2Async()
    {
        await Task.Delay(100).ConfigureAwait(false);

        return Sdk.SuppressInstrumentation;
    }

    [NonActivity(true)]
    public static TestAwaitable<bool> SuppressInstrumentationScopeAwaitable() => new(SuppressInstrumentationScope);

    [ActivityName]
    public static Tuple<string?, int> GetActivityName() => InternalGetActivityName();

    private static Tuple<string?, int> InternalGetActivityName()
    {
        var field = typeof(ActivityAttribute).Assembly.GetType("OpenTelemetry.Proxy.ActivityName")
            ?.GetField("Name", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(field);

        var name = field.GetValue(null);

        Assert.NotNull(name);

        var nameHolder = name.GetType().GetProperty("Value")?.GetValue(name);

        return nameHolder == null
            ? new(null, 0)
            : new((string?)nameHolder.GetType().GetField("Name")?.GetValue(nameHolder),
                Assert.IsType<int>(nameHolder.GetType().GetField("AvailableTimes")?.GetValue(nameHolder)));
    }

    [ActivityName]
    public static async ValueTask<Tuple<string?, int>> GetActivityNameAsync()
    {
        var field = typeof(ActivityAttribute).Assembly.GetType("OpenTelemetry.Proxy.ActivityName")
            ?.GetField("Name", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(field);

        var name = field.GetValue(null);

        Assert.NotNull(name);

        var nameHolder = name.GetType().GetProperty("Value")?.GetValue(name);

        await Task.Delay(100).ConfigureAwait(false);

        return nameHolder == null
            ? new(null, 0)
            : new((string?)nameHolder.GetType().GetField("Name")?.GetValue(nameHolder),
                Assert.IsType<int>(nameHolder.GetType().GetField("AvailableTimes")?.GetValue(nameHolder)));
    }

    [ActivityName]
    public static TestAwaitable<Tuple<string?, int>> GetActivityNameAwaitable() => new(InternalGetActivityName);

    [Activity]
    public static Activity? GetCurrentActivity() => Activity.Current;

    private static async Task<Activity?> CurrentActivityAsync()
    {
        await Task.Delay(100).ConfigureAwait(false);

        return Activity.Current;
    }

    [Activity]
    public static FSharpAsync<Activity?> GetCurrentActivityAsync() => FSharpAsync.AwaitTask(CurrentActivityAsync());

    [Activity]
    public static TestAwaitable<Activity?> GetCurrentActivityAwaitable() => new(static () => Activity.Current);

    public static void OutMethod(in int a, out int b, ref int c)
    {
        b = a * a;
        c = a * c;
    }
}
