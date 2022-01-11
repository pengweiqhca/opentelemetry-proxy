namespace OpenTelemetry.DynamicProxy;

internal interface IActivityInvoker
{
    public void Invoke(IInvocation invocation);
}
