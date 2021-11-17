namespace OpenTelemetry.DynamicProxy;

public interface IActivityInvokerFactory
{
    void Invoke(IInvocation invocation);
}

/// <summary>Instance should be singleton.</summary>
public class ActivityInvokerFactory : IActivityInvokerFactory, IDisposable
{
    private readonly IDictionary<MethodInfo, IActivityInvoker> _activityInvokers = new Dictionary<MethodInfo, IActivityInvoker>();
    private readonly ConcurrentDictionary<Type, ActivitySource> _activitySources = new();

    public void Invoke(IInvocation invocation)
    {
        if (!_activityInvokers.TryGetValue(invocation.Method, out var invoker))
            _activityInvokers[invocation.Method] = invoker = CreateActivityInvoker(invocation);

        invoker.Invoke(invocation);
    }

    private IActivityInvoker CreateActivityInvoker(IInvocation invocation)
    {
        var type = invocation.Method.ReflectedType ?? invocation.Method.DeclaringType ?? invocation.TargetType;

        return ActivityInvokerHelper.TryGetActivityName(invocation.Method, type, out var activityName, out var kind)
            ? (IActivityInvoker)Activator.CreateInstance(ActivityInvokerHelper.GetActivityInvokerType
                (invocation.Method.ReturnType), GetActivitySource(type), activityName, kind)
            : ActivityInvokerHelper.Noop;
    }

    private ActivitySource GetActivitySource(Type type) => _activitySources.GetOrAdd(type, static type =>
        new(ActivitySourceAttribute.GetActivitySourceName(type), type.Assembly.GetName().Version?.ToString() ?? ""));

    public void Dispose()
    {
        _activityInvokers.Clear();

        foreach (var activitySource in _activitySources.Values) activitySource.Dispose();

        _activitySources.Clear();
    }
}
