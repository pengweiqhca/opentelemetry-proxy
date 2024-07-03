using System.Runtime.CompilerServices;

namespace OpenTelemetry.Proxy;

public static class ActivityName
{
    private static readonly AsyncLocal<ActivityHolder?> Holder = new();
    private static readonly ConditionalWeakTable<Activity, ActivityHolder> ActivityTable = new();

    [StackTraceHidden]
    internal static void OnStart(Activity activity)
    {
        if (Holder.Value is not { } holder) return;

        var availableTimes = Interlocked.Read(ref holder.AvailableTimes);

        if (availableTimes == 0) return;

        if (availableTimes < 0)
        {
            holder.OnStart(activity);

            if (holder.HasOnEnd()) ActivityTable.Add(activity, holder);
        }

        availableTimes = Interlocked.Decrement(ref holder.AvailableTimes);

        if (availableTimes < 0) Interlocked.Exchange(ref holder.AvailableTimes, 0);
        else
        {
            holder.OnStart(activity);

            if (holder.HasOnEnd())
            {
                ActivityTable.Add(activity, holder);

                if (Interlocked.Read(ref holder.Waitings) >= 0)
                    Interlocked.Increment(ref holder.Waitings);
            }
            else if (availableTimes == 0) holder.Clear();
        }
    }

    [StackTraceHidden]
    internal static void OnEnd(Activity activity)
    {
        if (!ActivityTable.TryGetValue(activity, out var holder)) return;

        holder.OnEnd(activity);

        if (Interlocked.Decrement(ref holder.Waitings) == 0) holder.Clear();
    }

    public static IDisposable SetName(string name, IReadOnlyCollection<KeyValuePair<string, object?>>? tags = null,
        int readTimes = 1) => SetName(tags, name, readTimes);

    public static IDisposable SetName(string name, string tagName, object? tagValue, int readTimes = 1) =>
        SetName([new(tagName, tagValue)], name, readTimes);

    public static IDisposable SetName(IReadOnlyCollection<KeyValuePair<string, object?>> tags, int readTimes = 1) =>
        SetName(tags, null, readTimes);

    public static IDisposable SetName(IReadOnlyCollection<KeyValuePair<string, object?>>? tags, string? name,
        int readTimes = 1)
    {
        if (Holder.Value is { } holder)
        {
            // If the current holder hava no name, and the new holder only have name, then merge them.
            if (holder is ActivityNameHolder nameHolder && tags == null && name != null && nameHolder.Name == null)
                tags = nameHolder.Tags;

            holder.Clear();
        }

        Holder.Value = (tags != null || name != null) && readTimes != 0
            ? new ActivityNameHolder
            {
                Name = name,
                Tags = tags,
                AvailableTimes = readTimes,
                Waitings = readTimes < 0 ? -1 : 0
            }
            : null;

        return Disposable.Instance;
    }

    public static IDisposable OnEnd(Action<Activity> onEnd, int onEndTimes = 1)
    {
        Holder.Value?.Clear();

        Holder.Value = onEnd != default! && onEndTimes != 0
            ? new ActivityCallbackHolder
            {
                OnEndCallback = onEnd,
                AvailableTimes = onEndTimes,
                Waitings = onEndTimes < 0 ? -1 : 0
            }
            : null;

        return Disposable.Instance;
    }

    public static IDisposable On(Action<Activity> onStart, Action<Activity> onEnd, int onStartTimes = 1)
    {
        Holder.Value?.Clear();

        Holder.Value = (onStart != default! || onEnd != default!) && onStartTimes != 0
            ? new ActivityCallbackHolder
            {
                OnStartCallback = onStart,
                OnEndCallback = onEnd,
                AvailableTimes = onStartTimes,
                Waitings = onStartTimes < 0 ? -1 : 0
            }
            : null;

        return Disposable.Instance;
    }

    private abstract class ActivityHolder
    {
        public long AvailableTimes;

        public long Waitings = -1;

        public virtual bool HasOnEnd() => true;

        public abstract void Clear();

        public virtual void OnStart(Activity data) { }

        public abstract void OnEnd(Activity data);
    }

    private sealed class ActivityNameHolder : ActivityHolder
    {
        public string? Name;

        public IReadOnlyCollection<KeyValuePair<string, object?>>? Tags;

        public override void Clear()
        {
            Name = null;
            Tags = null;
            AvailableTimes = 0;
        }

        public override void OnEnd(Activity data)
        {
            if (Name != null) data.DisplayName = Name;

            if (Tags != null) data.SetTag(Tags);
        }
    }

    private sealed class ActivityCallbackHolder : ActivityHolder
    {
        public Action<Activity>? OnStartCallback;
        public Action<Activity>? OnEndCallback;

        public override bool HasOnEnd() => OnEndCallback != null;

        public override void Clear()
        {
            OnStartCallback = null;
            OnEndCallback = null;
            AvailableTimes = 0;
        }

        [StackTraceHidden]
        public override void OnStart(Activity data) => OnStartCallback?.Invoke(data);

        [StackTraceHidden]
        public override void OnEnd(Activity data) => OnEndCallback?.Invoke(data);
    }

    private sealed class Disposable : IDisposable
    {
        public static IDisposable Instance { get; } = new Disposable();

        public void Dispose()
        {
            Holder.Value?.Clear();

            Holder.Value = null;
        }
    }
}
