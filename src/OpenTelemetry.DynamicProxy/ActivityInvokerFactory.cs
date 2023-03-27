using OpenTelemetry.Proxy;

namespace OpenTelemetry.DynamicProxy;

/// <summary>Instance should be singleton.</summary>
public class ActivityInvokerFactory : IActivityInvokerFactory, IDisposable
{
    private readonly IDictionary<MethodInfo, IActivityInvoker> _activityInvokers = new Dictionary<MethodInfo, IActivityInvoker>();
    private readonly ConcurrentDictionary<Type, ActivitySource> _activitySources = new();

    public void Invoke(IInvocation invocation, ActivityType activityType)
    {
        if (!_activityInvokers.TryGetValue(invocation.Method, out var invoker))
        {
            invoker = CreateActivityInvoker(invocation, activityType);

            try
            {
                _activityInvokers[invocation.Method] = invoker;
            }
            catch (NullReferenceException)
            {
            }
        }

        invoker.Invoke(invocation);
    }

    private IActivityInvoker CreateActivityInvoker(IInvocation invocation, ActivityType activityType)
    {
        var type = invocation.Method.ReflectedType ?? invocation.Method.DeclaringType ?? invocation.TargetType;

        var settings = ActivityInvokerHelper.GetActivityName(invocation.Method, type, out var activityName, out var kind, out var maxUsableTimes);

        return settings switch
        {
            ActivitySettings.Activity => (IActivityInvoker)Activator.CreateInstance(ActivityInvokerHelper.GetActivityInvokerType(invocation.Method.ReturnType), GetActivitySource(type), activityName, kind)!,
            ActivitySettings.ActivityNameOnly => (IActivityInvoker)Activator.CreateInstance(ActivityInvokerHelper.GetActivityNameInvokerType(invocation.Method.ReturnType), activityName, maxUsableTimes, false)!,
            ActivitySettings.NonActivityAndSuppressInstrumentation => (IActivityInvoker)Activator.CreateInstance(ActivityInvokerHelper.GetActivityNameInvokerType(invocation.Method.ReturnType), activityName, maxUsableTimes, true)!,
            _ => activityType switch
            {
                ActivityType.ImplicitActivity => (IActivityInvoker)Activator.CreateInstance(ActivityInvokerHelper.GetActivityInvokerType(invocation.Method.ReturnType), GetActivitySource(type), null, ActivityKind.Internal)!,
                ActivityType.ImplicitActivityName => (IActivityInvoker)Activator.CreateInstance(ActivityInvokerHelper.GetActivityNameInvokerType(invocation.Method.ReturnType), null, 1, false)!,
                _ => ActivityInvokerHelper.Noop
            }
        };
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
