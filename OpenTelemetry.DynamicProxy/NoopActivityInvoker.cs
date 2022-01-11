namespace OpenTelemetry.DynamicProxy;

internal class NoopActivityInvoker : IActivityInvoker
{
    public void Invoke(IInvocation invocation) => invocation.Proceed();
}
