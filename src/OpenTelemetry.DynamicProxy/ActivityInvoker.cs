using Microsoft.Extensions.Internal;
using OpenTelemetry.Proxy;

namespace OpenTelemetry.DynamicProxy;

public class ActivityInvoker(
    ActivitySource activitySource,
    string activityName,
    ActivityKind kind,
    bool suppressInstrumentation,
    (Action<IInvocation, Activity>? BeforeProceed, Action<IInvocation, Activity>? AfterProceed) setTags,
    Action<Activity, object>? setReturnValueTag) : ActivityNameInvoker
{
    public string ActivitySourceName { get; } = activitySource.Name;

    public override void Invoke(IInvocation invocation)
    {
        if (activitySource.StartActivity(activityName, kind) is not { } activity)
        {
            if (suppressInstrumentation) base.Invoke(invocation);
            else invocation.Proceed();

            return;
        }

        setTags.BeforeProceed?.Invoke(invocation, activity);

        var disposable = suppressInstrumentation ? SuppressInstrumentationScope.Begin() : null;

        try
        {
            invocation.Proceed();
        }
        catch (Exception ex)
        {
            ActivityExtensions.SetExceptionStatus(activity, ex);

            disposable?.Dispose();
            activity.Dispose();

            throw;
        }

        setTags.AfterProceed?.Invoke(invocation, activity);

        disposable?.Dispose();

        var func = ActivityInvokerHelper.Convert(invocation.Method.ReturnType);
        if (func == null)
        {
            setReturnValueTag?.Invoke(activity, invocation.ReturnValue);

            activity.Dispose();

            return;
        }

        var awaiter = func(invocation.ReturnValue).GetAwaiter();

        if (awaiter.IsCompleted) ActivityAwaiter.OnCompleted(activity, awaiter, setReturnValueTag);
        else
        {
            Activity.Current = activity.Parent;

            awaiter.UnsafeOnCompleted(new ActivityAwaiter(activity, awaiter, setReturnValueTag).OnCompleted);
        }
    }

    private sealed class ActivityAwaiter(
        Activity activity,
        ObjectMethodExecutorAwaitable.Awaiter awaiter,
        Action<Activity, object>? setReturnValueTag)
    {
        public void OnCompleted() => OnCompleted(activity, awaiter, setReturnValueTag);

        public static void OnCompleted(Activity activity, ObjectMethodExecutorAwaitable.Awaiter awaiter, Action<Activity, object>? setReturnValueTag)
        {
            try
            {
                var result = awaiter.GetResult();

                setReturnValueTag?.Invoke(activity, result);
            }
            catch (Exception ex)
            {
                ActivityExtensions.SetExceptionStatus(activity, ex);
            }
            finally
            {
                activity.Dispose();
            }
        }
    }
}
