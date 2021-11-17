namespace OpenTelemetry.DynamicProxy;

public class OpenTelemetryInterceptor : IInterceptor
{
    private readonly IActivityInvokerFactory _invokerFactory;

    public OpenTelemetryInterceptor(IActivityInvokerFactory invokerFactory) =>
        _invokerFactory = invokerFactory;

    void IInterceptor.Intercept(IInvocation invocation) => _invokerFactory.Invoke(invocation);
}
