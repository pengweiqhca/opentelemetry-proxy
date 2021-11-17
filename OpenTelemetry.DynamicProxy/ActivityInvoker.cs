using OpenTelemetry.Trace;

namespace OpenTelemetry.DynamicProxy;

internal class ActivityInvoker : IActivityInvoker
{
    private readonly ActivitySource _activitySource;

    public string? ActivityName { get; }

    public ActivityKind Kind { get; }

    public ActivityInvoker(ActivitySource activitySource, string? activityName, ActivityKind kind)
    {
        _activitySource = activitySource;

        ActivityName = activityName;

        Kind = kind;
    }

    public void Invoke(IInvocation invocation)
    {
        Activity? activity;
        if (!_activitySource.HasListeners() ||
            (activity = _activitySource.StartActivity(string.IsNullOrWhiteSpace(ActivityName)
                ? $"{_activitySource.Name}.{invocation.Method.Name}"
                : ActivityName!, Kind, Activity.Current?.Context ?? default)) == null)
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
