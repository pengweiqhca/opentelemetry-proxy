namespace OpenTelemetry.Proxy;

public static class ActivityName
{
    private static readonly AsyncLocal<NameHolder?> Name = new();

    internal static (string? Name, IEnumerable<KeyValuePair<string, object?>>? Tags) GetName()
    {
        if (Name.Value is not { } holder || holder.AvailableTimes == 0) return default;

        if (holder.AvailableTimes < 0 || Interlocked.Decrement(ref holder.AvailableTimes) >= 0) return (holder.Name, holder.Tags);

        holder.Clear();

        return default;
    }

    [Obsolete("Old method.", true)]
    public static IDisposable SetName(string? name, int readTimes) => SetName(null, name, readTimes);

    public static IDisposable SetName(IReadOnlyCollection<KeyValuePair<string, object?>>? tags, string? name, int readTimes)
    {
        var holder = Name.Value;

        holder?.Clear();

        Name.Value = name != null && readTimes != 0
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

        public void Dispose() => SetName(null, null, 0);
    }
}
