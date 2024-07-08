namespace OpenTelemetry.Proxy;

public static class ActivityName
{
    public static IDisposable SetName(string name, IReadOnlyCollection<KeyValuePair<string, object?>>? tags = null,
        int setTimes = 1) => SetName(tags, name, setTimes);

    public static IDisposable SetName(string name, string tagName, object? tagValue, int setTimes = 1) =>
        SetName([new(tagName, tagValue)], name, setTimes);

    public static IDisposable SetName(IReadOnlyCollection<KeyValuePair<string, object?>> tags, int setTimes = 1) =>
        SetName(tags, null, setTimes);

    public static IDisposable SetName(IReadOnlyCollection<KeyValuePair<string, object?>>? tags, string? name,
        int setTimes = 1)
    {
        // If the current holder hava no name, and the new holder only have name, then merge them.
        if (InnerActivityAccessor.Activity is { OnStart.Target: ActivityNameHolder nameHolder } &&
            tags == null && name != null && nameHolder.Name == null)
            tags = nameHolder.Tags;

        nameHolder = new(name, tags, setTimes);

        return InnerActivityAccessor.OnActivity(nameHolder.OnStart, nameHolder.OnEnd);
    }

    private sealed class ActivityNameHolder(
        string? name,
        IReadOnlyCollection<KeyValuePair<string, object?>>? tags,
        long availableTimes)
    {
        private int _waitingItems;

        public string? Name => name;

        public IReadOnlyCollection<KeyValuePair<string, object?>>? Tags => tags;

        public bool OnStart(Activity _)
        {
            var times = Interlocked.Read(ref availableTimes);

            if (times == 0) return false;

            if (times < 0 || Interlocked.Decrement(ref availableTimes) >= 0)
            {
                Interlocked.Increment(ref _waitingItems);

                return true;
            }

            // rollback Decrement
            Interlocked.Increment(ref availableTimes);

            return false;
        }

        public bool OnEnd(Activity data)
        {
            if (name != null) data.DisplayName = name;

            if (tags != null) data.SetTag(tags);

            return Interlocked.Decrement(ref _waitingItems) > 0;
        }
    }
}
