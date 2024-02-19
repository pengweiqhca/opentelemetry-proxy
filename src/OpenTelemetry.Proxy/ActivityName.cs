namespace OpenTelemetry.Proxy;

public static class ActivityName
{
    private static readonly AsyncLocal<ActivityOnEndHolder?> Holder = new();

    internal static void OnEnd(Activity activity)
    {
        if (Holder.Value is not { } holder) return;

        var availableTimes = Interlocked.Read(ref holder.AvailableTimes);

        if (availableTimes == 0) return;

        if (availableTimes < 0) holder.OnEnd(activity);

        availableTimes = Interlocked.Decrement(ref holder.AvailableTimes);

        if (availableTimes < 0) Interlocked.Exchange(ref holder.AvailableTimes, 0);
        else
        {
            holder.OnEnd(activity);

            if (availableTimes == 0) holder.Clear();
        }
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
            ? new ActivityNameHolder { Name = name, Tags = tags, AvailableTimes = readTimes }
            : null;

        return Disposable.Instance;
    }

    public static IDisposable OnEnd(Action<Activity> onEnd, int readTimes = 1)
    {
        Holder.Value?.Clear();

        Holder.Value = onEnd != default! && readTimes != 0
            ? new ActivityCallbackHolder { Callback = onEnd, AvailableTimes = readTimes }
            : null;

        return Disposable.Instance;
    }

    private abstract class ActivityOnEndHolder
    {
        public long AvailableTimes;

        public abstract void Clear();

        public abstract void OnEnd(Activity data);
    }

    private sealed class ActivityNameHolder : ActivityOnEndHolder
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

    private sealed class ActivityCallbackHolder: ActivityOnEndHolder
    {
        public Action<Activity>? Callback;

        public override void Clear()
        {
            Callback = null;
            AvailableTimes = 0;
        }

        public override void OnEnd(Activity data) => Callback?.Invoke(data);
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
