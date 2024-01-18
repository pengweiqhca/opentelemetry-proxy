namespace OpenTelemetry.DynamicProxy;

public class ActivityInterceptor(IActivityInvokerFactory invokerFactory, ActivityType activityType = 0) : IInterceptor
{
    void IInterceptor.Intercept(IInvocation invocation) => invokerFactory.Invoke(invocation, activityType);
}
