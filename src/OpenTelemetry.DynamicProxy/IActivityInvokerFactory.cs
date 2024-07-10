namespace OpenTelemetry.DynamicProxy;

public interface IActivityInvokerFactory
{
    void Invoke(IInvocation invocation, ImplicitActivityContext context);
}
