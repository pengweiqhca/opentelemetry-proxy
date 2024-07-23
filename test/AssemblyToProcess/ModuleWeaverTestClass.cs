using Fasterflect;
using Microsoft.FSharp.Control;
using OpenTelemetry;
using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.Tests.Common;
using OpenTelemetry.Trace;
using System.Reflection;
using System.Runtime.CompilerServices;
using ActivityName =
    System.Tuple<string?, System.Collections.Generic.IReadOnlyCollection<
        System.Collections.Generic.KeyValuePair<string, object?>>?, long>;

namespace AssemblyToProcess;

[ActivitySource("ModuleWeaverTestClass", IncludeNonAsyncStateMachineMethod = true)]
public static class ModuleWeaverTestClass
{
    [NonActivity(true)]
    public static bool SuppressInstrumentationScope() => Sdk.SuppressInstrumentation;

    [Activity(SuppressInstrumentation = true)]
    public static bool SuppressInstrumentationScope2() => Sdk.SuppressInstrumentation;

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

    [Activity(SuppressInstrumentation = true)]
    public static async Task<bool> SuppressInstrumentationScope3Async()
    {
        await Task.Delay(100).ConfigureAwait(false);

        return Sdk.SuppressInstrumentation;
    }

    [NonActivity(true)]
    public static TestAwaitable<bool> SuppressInstrumentationScopeAwaitable() => new(SuppressInstrumentationScope);

    [ActivityName]
    public static ActivityName GetActivityName([ActivityTag] int delay) => InternalGetActivityName();

    [Activity]
    public static T? GenericMethod<T>() => default;

    [Activity]
    public static void VoidMethod()
    {
        if (DateTime.Now is { Hour: > 10, Second: < 10 }) { }
    }

    private static ActivityName InternalGetActivityName()
    {
        var holder = typeof(InnerActivityAccessor).GetProperty("Activity", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null);
        if (holder == null) return new(null, default, 0);

        var nameHolder = Assert.IsAssignableFrom<Delegate>(holder.GetPropertyValue("OnStart")).Target;

        Assert.NotNull(nameHolder);

        return new(nameHolder.GetPropertyValue("Name") as string,
            nameHolder.GetPropertyValue("Tags") as IReadOnlyCollection<KeyValuePair<string, object?>>,
            Assert.IsType<long>(nameHolder.GetFieldValue("<availableTimes>P", Flags.NonPublic | Flags.Instance)));
    }

    [ActivityName(Tags = [nameof(delay)])]
    public static async ValueTask<ActivityName> GetActivityNameAsync(int delay)
    {
        await Task.Delay(100).ConfigureAwait(false);

        return InternalGetActivityName();
    }

    [ActivityName(Tags = [nameof(Now)])]
    public static TestAwaitable<ActivityName> GetActivityNameAwaitable(int delay) =>
        new(InternalGetActivityName);

    [Activity(Tags = [nameof(delay)])]
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
    public static async Task<Activity?> AwaitGetCurrentActivityAsync([ActivityTag] int delay) =>
        delay < 10 ? Activity.Current : await CurrentActivityAsync(delay).ConfigureAwait(false);

    public static DateTime Now { get; } = new(2024, 1, 1);

    [Activity(Tags = [nameof(Now)])]
    public static FSharpAsync<Activity?> GetCurrentActivityFSharpAsync(int delay) =>
        FSharpAsync.AwaitTask(CurrentActivityAsync(delay));

    [Activity(Tags = [nameof(Now)])]
    public static TestAwaitable<Activity?> GetCurrentActivityAwaitable([ActivityTag] int delay) =>
        new(static () => Activity.Current);

    [Activity(SuppressInstrumentation = true)]
    public static Task Exception() => Task.FromException(new());

    [return: ActivityTag("def")]
    public static int OutMethod(in int a, out int b, ref int c, int d, int e, [ActivityTag] int f)
    {
        b = a * a;
        c = a * c;

        Console.WriteLine(A()?.ToString());

        return d + e + f;
    }

    private static object? A() => null;

    [Activity(Tags = [ActivityTagAttribute.ReturnValueTagName])]
    public static Task<int> ReturnValue(int a) => Task.FromResult(a + 1);

    [ActivityName]
    public static bool Using(out DateTimeOffset now)
    {
        using (OpenTelemetry.Proxy.ActivityName.SetName("Using"))
        {
            now = DateTimeOffset.Now;
        }

        return true;
    }

    public delegate bool TestDelegate(out DateTimeOffset now);
}
