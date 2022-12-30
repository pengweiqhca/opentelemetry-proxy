using OpenTelemetry.Proxy;

namespace OpenTelemetry.DynamicProxy;

public class ActivityInterceptor : IInterceptor
{
    private readonly IActivityInvokerFactory _invokerFactory;
    private readonly ActivityType _activityType;

    public ActivityInterceptor(IActivityInvokerFactory invokerFactory) : this(invokerFactory, ActivityType.Explicit) { }

    public ActivityInterceptor(IActivityInvokerFactory invokerFactory, ActivityType activityType)
    {
        _invokerFactory = invokerFactory;

        _activityType = activityType;
    }

    void IInterceptor.Intercept(IInvocation invocation) => _invokerFactory.Invoke(invocation, _activityType);
}
