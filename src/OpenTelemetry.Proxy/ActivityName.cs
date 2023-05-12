namespace OpenTelemetry.Proxy;

public static class ActivityName
{
    private static readonly AsyncLocal<NameHolder?> Name = new();

    internal static (string? Name, IEnumerable<KeyValuePair<string, object?>>? Tags) GetName()
    {
        if (Name.Value is not { } holder || holder.AvailableTimes == 0) return default;

        if (holder.AvailableTimes < 0 || Interlocked.Decrement(ref holder.AvailableTimes) >= 0)
            return (holder.Name, holder.Tags);

        holder.Clear();

        return default;
    }

    public static IDisposable SetName(string name, IReadOnlyCollection<KeyValuePair<string, object?>>? tags = null,
        int readTimes = 1) => SetName(tags, name, readTimes);

    public static IDisposable SetName(IReadOnlyCollection<KeyValuePair<string, object?>> tags, int readTimes = 1) =>
        SetName(tags, null, readTimes);

    public static IDisposable SetName(IReadOnlyCollection<KeyValuePair<string, object?>>? tags, string? name,
        int readTimes = 1)
    {
        var holder = Name.Value;

        holder?.Clear();

        Name.Value = (tags != null || name != null) && readTimes != 0
            ? new() { Name = name, Tags = tags, AvailableTimes = readTimes }
            : null;

        return Disposable.Instance;
    }

    private sealed class NameHolder
    {
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public string? Name;

        // ReSharper disable once MemberHidesStaticFromOuterClass
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
