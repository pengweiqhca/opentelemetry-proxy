namespace OpenTelemetry.DynamicProxy;

internal static class ActivityName
{
    private static readonly AsyncLocal<NameHolder> Name = new();

    public static string? GetName()
    {
        if (Name.Value is not { } name || name.AvailableTimes == 0) return null;

        if (name.AvailableTimes < 0 || Interlocked.Decrement(ref name.AvailableTimes) >= 0)
            return name.Name;

        name.AvailableTimes = 0;

        return null;
    }

    public static void SetName(string? name, int readTimes)
    {
        var holder = Name.Value;

        if (holder != null) holder.Name = default;

        if (name != null) Name.Value = new() { Name = name, AvailableTimes = readTimes };
    }

    private class NameHolder
    {
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public string? Name;
        public int AvailableTimes;
    }
}
