namespace OpenTelemetry.DynamicProxy;

public readonly struct ImplicitActivityContext(ImplicitActivityType type)
{
    public ImplicitActivityType Type { get; } = type;

    public string? ActivitySourceName { get; }

    public ActivityKind ActivityKind { get; }

    public bool SuppressInstrumentation { get; init; }

    public Action<IInvocation, Activity>? BeforeProceed { get; init; }

    public Action<IInvocation, Activity>? AfterProceed { get; init; }

    public string? ReturnValueTagName { get; init; }

    public ImplicitActivityContext(string? activitySourceName, ActivityKind activityKind)
        : this(ImplicitActivityType.Activity)
    {
        ActivitySourceName = activitySourceName;
        ActivityKind = activityKind;
    }
}
