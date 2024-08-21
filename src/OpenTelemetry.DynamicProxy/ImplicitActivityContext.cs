namespace OpenTelemetry.DynamicProxy;

public readonly struct ImplicitActivityContext(ImplicitActivityType type)
{
    public ImplicitActivityType Type { get; } = type;

    public string? ActivitySourceName { get; }

    public string? ActivityBaseName { get; }

    public ActivityKind ActivityKind { get; }

    public bool SuppressInstrumentation { get; init; }

    public Action<IInvocation, Activity>? BeforeProceed { get; init; }

    public Action<IInvocation, Activity>? AfterProceed { get; init; }

    public Action<Activity, object>? SetReturnValueTag { get; init; }

    public ImplicitActivityContext(string? activitySourceName, ActivityKind activityKind)
        : this(ImplicitActivityType.Activity)
    {
        ActivitySourceName = activitySourceName;
        ActivityKind = activityKind;
    }

    public ImplicitActivityContext(string? activitySourceName, string? activityBaseName, ActivityKind activityKind)
        : this(ImplicitActivityType.Activity)
    {
        ActivitySourceName = activitySourceName;
        ActivityBaseName = string.IsNullOrWhiteSpace(activityBaseName) ? activitySourceName : activityBaseName;
        ActivityKind = activityKind;
    }
}
