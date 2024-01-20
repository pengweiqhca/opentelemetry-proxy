using OpenTelemetry.Proxy;

namespace OpenTelemetry.DynamicProxy;

public class ActivityNameInvoker : IActivityInvoker
{
    private readonly string? _activityName;
    private readonly int _maxUsableTimes;
    private readonly Func<IInvocation, IReadOnlyCollection<KeyValuePair<string, object?>>?>? _getTags;

    public ActivityNameInvoker() { }

    public ActivityNameInvoker(string activityName, int maxUsableTimes,
        Func<IInvocation, IReadOnlyCollection<KeyValuePair<string, object?>>?>? getTags)
    {
        _activityName = activityName;
        _maxUsableTimes = maxUsableTimes;
        _getTags = getTags;
    }

    public void Invoke(IInvocation invocation)
    {
        var disposable = _activityName == null
            ? SuppressInstrumentationScope.Begin()
            : ActivityName.SetName(_getTags?.Invoke(invocation), _activityName, _maxUsableTimes);

        try
        {
            invocation.Proceed();
        }
        catch
        {
            disposable.Dispose();

            throw;
        }

        var func = ActivityInvokerHelper.Convert(invocation.Method.ReturnType);
        if (func == null)
        {
            disposable.Dispose();

            return;
        }

        var awaiter = func(invocation.ReturnValue).GetAwaiter();
        if (awaiter.IsCompleted) disposable.Dispose();
        else awaiter.UnsafeOnCompleted(disposable.Dispose);
    }
}
