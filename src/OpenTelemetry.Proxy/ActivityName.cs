namespace OpenTelemetry.Proxy;

public static class ActivityName
{
    private static readonly AsyncLocal<ValueHolder?> Holder = new();

    internal static (string? Name, IEnumerable<KeyValuePair<string, object?>>? Tags) GetName()
    {
        if (Holder.Value is not { } holder || holder.AvailableTimes == 0) return default;

        if (holder.AvailableTimes < 0 || Interlocked.Decrement(ref holder.AvailableTimes) >= 0)
            return (holder.Name, holder.Tags);

        holder.Clear();

        return default;
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
        var holder = Holder.Value;

        if (holder != null)
        {
            // If the current holder hava no name, and the new holder only have name, then merge them.
            if (tags == null && name != null && holder.Name == null) tags = holder.Tags;

            holder.Clear();
        }

        Holder.Value = (tags != null || name != null) && readTimes != 0
            ? new() { Name = name, Tags = tags, AvailableTimes = readTimes }
            : null;

        return Disposable.Instance;
    }

    private sealed class ValueHolder
    {
        public string? Name;

        public IReadOnlyCollection<KeyValuePair<string, object?>>? Tags;

        public int AvailableTimes;

        public void Clear()
        {
            Name = null;
            Tags = null;
            AvailableTimes = 0;
        }
    }

    private sealed class Disposable : IDisposable
    {
        public static IDisposable Instance { get; } = new Disposable();

        public void Dispose() => SetName(tags: null, null, 0);
    }
}
