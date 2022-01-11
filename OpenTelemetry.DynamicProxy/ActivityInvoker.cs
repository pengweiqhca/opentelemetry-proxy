using OpenTelemetry.Trace;

namespace OpenTelemetry.DynamicProxy;

internal class ActivityInvoker : IActivityInvoker
{
    private readonly ActivitySource _activitySource;

    private readonly ActivityKind _kind;

    private readonly string? _activityName;

    public ActivityInvoker(ActivitySource activitySource, string? activityName, ActivityKind kind)
    {
        _activitySource = activitySource;

        _kind = kind;

        _activityName = activityName;
    }

    public void Invoke(IInvocation invocation)
    {
        Activity? activity;
        if (!_activitySource.HasListeners() ||
            (activity = _activitySource.StartActivity(string.IsNullOrWhiteSpace(_activityName)
                ? $"{_activitySource.Name}.{invocation.Method.Name}"
                : _activityName!, _kind, Activity.Current?.Context ?? default)) == null)
        {
            invocation.Proceed();

            return;
        }

        activity.SetStatus(Status.Ok);

        try
        {
            invocation.Proceed();
        }
        catch (Exception ex)
        {
            OnException(activity, ex);

            throw;
        }

        InvokeAfter(invocation, activity);
    }

    protected static void OnException(Activity activity, Exception ex)
    {
        activity.SetStatus(Status.Error.WithDescription(ex.Message));

        activity.RecordException(ex);

        Stop(activity);
    }

    protected static void Stop(Activity activity)
    {
        activity.Stop();
        activity.Dispose();
    }

    protected virtual void InvokeAfter(IInvocation invocation, Activity activity) => Stop(activity);
}

internal class ActivityNameInvoker : IActivityInvoker
{
    private readonly string? _activityName;
    private readonly int _maxUseableTimes;
    private readonly bool _suppressInstrumentation;

    public ActivityNameInvoker(string? activityName, int maxUseableTimes, bool suppressInstrumentation)
    {
        _activityName = activityName;
        _maxUseableTimes = maxUseableTimes;
        _suppressInstrumentation = suppressInstrumentation;
    }

    public void Invoke(IInvocation invocation)
    {
        if (_suppressInstrumentation)
        {
            var disposable = SuppressInstrumentationScope.Begin();

            try
            {
                invocation.Proceed();
            }
            catch
            {
                disposable.Dispose();

                throw;
            }

            InvokeAfter(invocation, disposable);
        }
        else
        {
            ActivityName.SetName(string.IsNullOrWhiteSpace(_activityName) ? $"{invocation.TargetType.FullName}.{invocation.Method.Name}" : _activityName, _maxUseableTimes);

            try
            {
                invocation.Proceed();
            }
            catch
            {
                ActivityName.SetName(null, 0);

                throw;
            }

            InvokeAfter(invocation, null);
        }
    }

    protected virtual void InvokeAfter(IInvocation invocation, IDisposable? disposable)
    {
        if (disposable == null) ActivityName.SetName(null, 0);
        else disposable.Dispose();
    }
}
