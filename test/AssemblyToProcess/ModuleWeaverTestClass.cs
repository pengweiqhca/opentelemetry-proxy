using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
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
    public static Tuple<string?, IReadOnlyCollection<KeyValuePair<string, object?>>?, int> GetActivityName([ActivityTag] int delay) => InternalGetActivityName();

    private static Tuple<string?, IReadOnlyCollection<KeyValuePair<string, object?>>?, int> InternalGetActivityName()
    {
        var field = typeof(ActivityAttribute).Assembly.GetType("OpenTelemetry.Proxy.ActivityName")
            ?.GetField("Name", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(field);

        var name = field.GetValue(null);

        Assert.NotNull(name);

        var nameHolder = name.GetType().GetProperty("Value")?.GetValue(name);

        return nameHolder == null
            ? new(null, default, 0)
            : new(nameHolder.GetType().GetField("Name")?.GetValue(nameHolder) as string,
                nameHolder.GetType().GetField("Tags")?.GetValue(nameHolder) as IReadOnlyCollection<KeyValuePair<string, object?>>,
                Assert.IsType<int>(nameHolder.GetType().GetField("AvailableTimes")?.GetValue(nameHolder)));
    }

    [ActivityName(Tags = new[] { nameof(delay) })]
    public static async ValueTask<Tuple<string?, IReadOnlyCollection<KeyValuePair<string, object?>>?, int>>
        GetActivityNameAsync(int delay)
    {
        await Task.Delay(100).ConfigureAwait(false);

        return InternalGetActivityName();
    }

    [ActivityName(Tags = new[] { nameof(Now) })]
    public static TestAwaitable<Tuple<string?, IReadOnlyCollection<KeyValuePair<string, object?>>?, int>> GetActivityNameAwaitable(int delay) =>
        new(InternalGetActivityName);

    [Activity(Tags = new[] { nameof(delay) })]
    public static Activity? GetCurrentActivity(int delay) => Activity.Current;

    private static async Task<Activity?> CurrentActivityAsync()
    {
        await Task.Delay(100).ConfigureAwait(false);

        return Activity.Current;
    }

    [Activity]
    public static async Task<Activity?> GetCurrentActivityAsync([ActivityTag] int delay) =>
        await CurrentActivityAsync().ConfigureAwait(false);

    public static DateTime Now { get; } = new(2024, 1, 1);

    [Activity(Tags = new[] { nameof(Now) })]
    public static FSharpAsync<Activity?> GetCurrentActivityFSharpAsync(int delay) =>
        FSharpAsync.AwaitTask(CurrentActivityAsync());

    [Activity(Tags = new[] { nameof(Now) })]
    public static TestAwaitable<Activity?> GetCurrentActivityAwaitable([ActivityTag] int delay) =>
        new(static () => Activity.Current);

    [Activity]
    public static Task Exception() => Task.FromException(new());

    public static void OutMethod(in int a, out int b, ref int c)
    {
        b = a * a;
        c = a * c;
    }
}
