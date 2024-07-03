namespace OpenTelemetry.DynamicProxy;

public class ActivityInterceptor(IActivityInvokerFactory invokerFactory, InvokeContext context) : IInterceptor
{
    public ActivityInterceptor(IActivityInvokerFactory invokerFactory, ActivityType activityType = 0)
        : this(invokerFactory, new InvokeContext(activityType)) { }

    void IInterceptor.Intercept(IInvocation invocation) => invokerFactory.Invoke(invocation, context);
}
