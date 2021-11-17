namespace OpenTelemetry.DynamicProxy;

internal interface IActivityInvoker
{
    string? ActivityName { get; }

    ActivityKind Kind { get; }

    public void Invoke(IInvocation invocation);
}
