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

    [Activity]
    public static Task Exception() => Task.FromException(new());

    private static ActivitySource ActivitySource { get; } = new("ModuleWeaverTestClass");

    [NonActivity]
    public static Task Exception2()
    {
        var activity = ActivitySource.StartActivity("ModuleWeaverTestClass.Exception");
        var disposable = OpenTelemetry.SuppressInstrumentationScope.Begin();

        Task task;
        try
        {
            task = Task.FromException(new Exception());
        }
        catch (Exception ex)
        {
            disposable?.Dispose();

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message).RecordException(ex);

            throw;
        }

        if (activity != null) ActivityAwaiter2.OnCompleted(task.GetAwaiter(), activity, disposable);

        return task;
    }

    private sealed class ActivityAwaiter2
    {
        private readonly TaskAwaiter _awaiter;

        private readonly Activity _activity;
        private readonly IDisposable _disposable;

        private ActivityAwaiter2(TaskAwaiter awaiter, Activity activity, IDisposable disposable)
        {
            _awaiter = awaiter;
            _activity = activity;
            _disposable = disposable;

            Activity.Current = activity.Parent;
        }

        private void OnCompleted() => Completed(_awaiter, _activity, _disposable);

        private static void Completed(TaskAwaiter awaiter, Activity activity, IDisposable disposable)
        {
            try
            {
                awaiter.GetResult();
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatusCode.Error, ex.Message).RecordException(ex);
            }
            finally
            {
                disposable?.Dispose();
                activity.Dispose();
            }
        }

        public static void OnCompleted(TaskAwaiter awaiter, Activity activity, IDisposable disposable)
        {
            if (awaiter.IsCompleted) Completed(awaiter, activity, disposable);
            else awaiter.UnsafeOnCompleted(new ActivityAwaiter2(awaiter, activity, disposable).OnCompleted);
        }
    }

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
