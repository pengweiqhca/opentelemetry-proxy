namespace OpenTelemetry.DynamicProxy;

public readonly struct InvokeContext(ActivityType activityType)
{
    public ActivityType ActivityType { get; } = activityType;

    public string? ImplicitActivitySourceName { get; }

    public ActivityKind ImplicitActivityKind { get; }

    public InvokeContext(string? implicitActivitySourceName, ActivityKind implicitActivityKind)
        : this(ActivityType.ImplicitActivity)
    {
        ImplicitActivitySourceName = implicitActivitySourceName;
        ImplicitActivityKind = implicitActivityKind;
    }
}
