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

    public static void SetName(string? name, int readTimes)
    {
        var holder = Name.Value;

        if (holder != null) holder.AvailableTimes = 0;

        Name.Value = name != null && readTimes > 0 ? new() { Name = name, AvailableTimes = readTimes } : null;
    }

    public static void Clear() => SetName(null, 0);

    private class NameHolder
    {
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public string? Name;
        public int AvailableTimes;
    }
}
