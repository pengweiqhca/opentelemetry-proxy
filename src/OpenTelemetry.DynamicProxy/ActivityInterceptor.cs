namespace OpenTelemetry.DynamicProxy;

public class ActivityInterceptor(IActivityInvokerFactory invokerFactory) : IInterceptor
{
    private readonly ImplicitActivityContext _context;

    public ActivityInterceptor(IActivityInvokerFactory invokerFactory, ImplicitActivityContext context)
        : this(invokerFactory) => _context = context;

    void IInterceptor.Intercept(IInvocation invocation) => invokerFactory.Invoke(invocation, _context);
}
