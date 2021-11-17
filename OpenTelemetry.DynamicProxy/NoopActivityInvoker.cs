namespace OpenTelemetry.DynamicProxy;

internal class NoopActivityInvoker : IActivityInvoker
{
    public string? ActivityName => null;

    public ActivityKind Kind => ActivityKind.Internal;

    public void Invoke(IInvocation invocation) => invocation.Proceed();
}
