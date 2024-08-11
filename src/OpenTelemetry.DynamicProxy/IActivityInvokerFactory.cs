namespace OpenTelemetry.DynamicProxy;

public interface IActivityInvokerFactory
{
    IActivityInvoker Create(IInvocation invocation, ImplicitActivityContext context);
}
