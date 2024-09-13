using System.Runtime.CompilerServices;

namespace OpenTelemetry.Proxy;

public static class InnerActivityAccessor
{
    private static readonly AsyncLocal<ActivityHolder?> Holder = new();
    private static readonly ConditionalWeakTable<Activity, ActivityHolder> ActivityTable = new();

    internal static ActivityHolder? Activity => Holder.Value;

    [StackTraceHidden]
    internal static void ActivityStarted(Activity activity)
    {
        if (Holder.Value is not { } holder) return;

        if (holder.OnActivityStart(activity) && holder.HasOnEnd())
            ActivityTable.Add(activity, holder);
        else holder.Clear();
    }

    [StackTraceHidden]
    internal static void ActivityEnded(Activity activity)
    {
        if (!ActivityTable.TryGetValue(activity, out var holder)) return;

        if (holder.OnActivityEnd(activity)) holder.Clear();
    }

    public static IDisposable OnActivityStart(Action<Activity> onStart) =>
        OnActivityStart(new Action2Func(onStart).Invoke);

    public static IDisposable OnActivityStart(Func<Activity, bool> onStart)
    {
        Holder.Value?.Clear();

        var holder = Holder.Value = new(onStart, null);

        return new Disposable(holder);
    }

    public static IDisposable OnActivityEnd(Action<Activity> onEnd) => OnActivityEnd(new Action2Func(onEnd).Invoke);

    public static IDisposable OnActivityEnd(Func<Activity, bool> onEnd)
    {
        Holder.Value?.Clear();

        var holder = Holder.Value = new(null, onEnd);

        return new Disposable(holder);
    }

    public static IDisposable OnActivity(Action<Activity> onStart, Action<Activity> onEnd) =>
        OnActivity(new Action2Func(onStart).Invoke, new Action2Func(onEnd).Invoke);

    public static IDisposable OnActivity(Func<Activity, bool> onStart, Action<Activity> onEnd) =>
        OnActivity(onStart, new Action2Func(onEnd).Invoke);

    public static IDisposable OnActivity(Action<Activity> onStart, Func<Activity, bool> onEnd) =>
        OnActivity(new Action2Func(onStart).Invoke, onEnd);

    public static IDisposable OnActivity(Func<Activity, bool> onStart, Func<Activity, bool> onEnd)
    {
        Holder.Value?.Clear();

        var holder = Holder.Value = new(onStart, onEnd);

        return new Disposable(holder);
    }

    private sealed class Action2Func(Action<Activity> action)
    {
        public bool Invoke(Activity activity)
        {
            action(activity);

            return true;
        }
    }

    [StackTraceHidden]
    public static IDisposable SetContext(string name, IReadOnlyCollection<KeyValuePair<string, object?>>? tags = null,
        bool adjustStartTime = false) =>
        SetContext(new InnerActivityContext { Name = name, Tags = tags, AdjustStartTime = adjustStartTime });

    [StackTraceHidden]
    public static IDisposable SetContext(string name, string tagName, object? tagValue) =>
        SetContext(new InnerActivityContext { Name = name, Tags = [new(tagName, tagValue)] });

    [StackTraceHidden]
    public static IDisposable SetContext(IReadOnlyCollection<KeyValuePair<string, object?>> tags) =>
        SetContext(new InnerActivityContext { Tags = tags });

    public static IDisposable SetContext(InnerActivityContext context)
    {
        // If the current holder hava no name, and the new holder only have name, then merge them.
        if (InnerActivityAccessor.Activity is { OnStart.Target: InnerActivityContext outerContext })
            context.Merge(outerContext);

        return InnerActivityAccessor.OnActivity(context.OnStart, context.OnEnd);
    }

    internal sealed class ActivityHolder(Func<Activity, bool>? onStart, Func<Activity, bool>? onEnd)
    {
        private Func<Activity, bool>? _onEnd = onEnd;

        public bool HasOnEnd() => _onEnd != null;

        public void Clear()
        {
            OnStart = null;
            _onEnd = null;
        }

        public Func<Activity, bool>? OnStart { get; private set; } = onStart;

        [StackTraceHidden]
        public bool OnActivityStart(Activity data) => Invoke(OnStart, data);

        [StackTraceHidden]
        public bool OnActivityEnd(Activity data) => Invoke(_onEnd, data);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Invoke(Func<Activity, bool>? func, Activity data) => func == null || func(data);
    }

    private sealed class Disposable(ActivityHolder holder) : IDisposable
    {
        public void Dispose()
        {
            holder.Clear();

            Holder.Value = null;
        }
    }
}
