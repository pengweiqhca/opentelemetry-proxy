using Microsoft.FSharp.Control;
using OpenTelemetry;
using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.Tests.Common;
using System.Reflection;
using ActivityName =
    System.Tuple<string?, System.Collections.Generic.IReadOnlyCollection<
        System.Collections.Generic.KeyValuePair<string, object?>>?, int>;

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
    public static ActivityName GetActivityName([ActivityTag] int delay) => InternalGetActivityName();

    private static ActivityName InternalGetActivityName()
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
                nameHolder.GetType().GetField("Tags")?.GetValue(nameHolder) as
                    IReadOnlyCollection<KeyValuePair<string, object?>>,
                Assert.IsType<int>(nameHolder.GetType().GetField("AvailableTimes")?.GetValue(nameHolder)));
    }

    [ActivityName(Tags = new[] { nameof(delay) })]
    public static async ValueTask<ActivityName> GetActivityNameAsync(int delay)
    {
        await Task.Delay(100).ConfigureAwait(false);

        return InternalGetActivityName();
    }

    [ActivityName(Tags = new[] { nameof(Now) })]
    public static TestAwaitable<ActivityName> GetActivityNameAwaitable(int delay) =>
        new(InternalGetActivityName);

    [Activity(Tags = new[] { nameof(delay) })]
    public static Activity? GetCurrentActivity(int delay) => Activity.Current;

    private static async Task<Activity?> CurrentActivityAsync(int delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);

        return Activity.Current;
    }

    [Activity]
    public static Task<Activity?> GetCurrentActivityAsync([ActivityTag] int delay) =>
        delay < 10 ? Task.FromResult(Activity.Current) : CurrentActivityAsync(delay);

    [Activity]
    public static Task<Activity?> GetCurrentActivity2Async([ActivityTag] int delay)
    {
        if (delay < 10)
            return Task.FromResult(Activity.Current);

        Console.WriteLine(DateTime.Now);

        return CurrentActivityAsync(delay);
    }

    [Activity]
    public static async Task<Activity?> AWaitGetCurrentActivityAsync([ActivityTag] int delay) =>
        delay < 10 ? Activity.Current : await CurrentActivityAsync(delay).ConfigureAwait(false);

    public static DateTime Now { get; } = new(2024, 1, 1);

    [Activity(Tags = new[] { nameof(Now) })]
    public static FSharpAsync<Activity?> GetCurrentActivityFSharpAsync(int delay) =>
        FSharpAsync.AwaitTask(CurrentActivityAsync(delay));

    [Activity(Tags = new[] { nameof(Now) })]
    public static TestAwaitable<Activity?> GetCurrentActivityAwaitable([ActivityTag] int delay) =>
        new(static () => Activity.Current);

    [Activity]
    public static Task Exception() => Task.FromException(new());

    [return: ActivityTag("def")]
    public static int OutMethod(in int a, out int b, ref int c, int d, int e, [ActivityTag] int f)
    {
        b = a * a;
        c = a * c;

        return d + e + f;
    }

    [Activity(Tags = new[] { ActivityTagAttribute.ReturnValueTagName })]
    public static Task<int> ReturnValue(int a) => Task.FromResult(a + 1);
}
