using OpenTelemetry.Proxy;

namespace OpenTelemetry.DynamicProxy;

public class ActivityNameInvoker : IActivityInvoker
{
    private readonly string? _activityName;
    private readonly bool _adjustStartTime;
    private readonly Func<IInvocation, IReadOnlyCollection<KeyValuePair<string, object?>>?>? _getTags;

    public ActivityNameInvoker() { }

    public ActivityNameInvoker(string activityName, bool adjustStartTime,
        Func<IInvocation, IReadOnlyCollection<KeyValuePair<string, object?>>?>? getTags)
    {
        _activityName = activityName;
        _adjustStartTime = adjustStartTime;
        _getTags = getTags;
    }

    public virtual void Invoke(IInvocation invocation)
    {
        var disposable = _activityName == null
            ? SuppressInstrumentationScope.Begin()
            : InnerActivityAccessor.SetActivityContext(new()
            {
                AdjustStartTime = _adjustStartTime,
                Name = _activityName,
                Tags = _getTags?.Invoke(invocation)
            });

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
