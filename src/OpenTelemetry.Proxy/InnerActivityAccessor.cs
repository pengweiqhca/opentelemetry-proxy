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
        if (Holder.Value is not { } holder || !holder.OnActivityStart(activity)) return;

        if (holder.HasOnEnd()) ActivityTable.Add(activity, holder);
        else holder.Clear();
    }

    [StackTraceHidden]
    internal static void ActivityEnded(Activity activity)
    {
        if (!ActivityTable.TryGetValue(activity, out var holder)) return;

        if (holder.OnActivityEnd(activity)) holder.Clear();
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

    internal sealed class ActivityHolder(Func<Activity, bool>? onStart, Func<Activity, bool>? onEnd)
    {
        public bool HasOnEnd() => onEnd != null;

        public void Clear()
        {
            onStart = null;
            onEnd = null;
        }

        public Func<Activity, bool>? OnStart => onStart;

        [StackTraceHidden]
        public bool OnActivityStart(Activity data) => onStart == null || onStart(data);

        [StackTraceHidden]
        public bool OnActivityEnd(Activity data) => onEnd == null || onEnd(data);
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
