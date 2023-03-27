namespace OpenTelemetry.DynamicProxy;

public interface IActivityInvoker
{
    public void Invoke(IInvocation invocation);
}
