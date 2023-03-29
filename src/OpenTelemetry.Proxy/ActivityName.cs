namespace OpenTelemetry.Proxy;

public static class ActivityName
{
    private static readonly AsyncLocal<NameHolder?> Name = new();

    internal static string? GetName()
    {
        if (Name.Value is not { } name || name.AvailableTimes == 0) return null;

        if (name.AvailableTimes < 0 || Interlocked.Decrement(ref name.AvailableTimes) >= 0) return name.Name;

        name.AvailableTimes = 0;

        Name.Value = null;

        return null;
    }

    public static IDisposable SetName(string? name, int readTimes)
    {
        var holder = Name.Value;

        if (holder != null) holder.AvailableTimes = 0;

        Name.Value = name != null && readTimes > 0 ? new() { Name = name, AvailableTimes = readTimes } : null;

        return Disposable.Instance;
    }

    private sealed class NameHolder
    {
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public string? Name;
        public int AvailableTimes;
    }

    private sealed class Disposable : IDisposable
    {
        public static IDisposable Instance { get; } = new Disposable();

        public void Dispose() => SetName(null, 0);
    }
}
