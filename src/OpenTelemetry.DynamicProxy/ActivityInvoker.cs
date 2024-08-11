using Microsoft.Extensions.Internal;
using OpenTelemetry.Proxy;

namespace OpenTelemetry.DynamicProxy;

public class ActivityInvoker(
    ActivitySource activitySource,
    string activityName,
    ActivityKind kind,
    bool suppressInstrumentation,
    (Action<IInvocation, Activity>? BeforeProceed, Action<IInvocation, Activity>? AfterProceed) setTags,
    string? returnValueTagName) : ActivityNameInvoker
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
            if (returnValueTagName != null) activity.SetTagEnumerable(returnValueTagName, invocation.ReturnValue);

            activity.Dispose();

            return;
        }

        var awaiter = func(invocation.ReturnValue).GetAwaiter();

        if (awaiter.IsCompleted) ActivityAwaiter.OnCompleted(activity, awaiter, returnValueTagName);
        else
        {
            Activity.Current = activity.Parent;

            awaiter.UnsafeOnCompleted(new ActivityAwaiter(activity, awaiter, returnValueTagName).OnCompleted);
        }
    }

    private sealed class ActivityAwaiter(
        Activity activity,
        ObjectMethodExecutorAwaitable.Awaiter awaiter,
        string? returnValueTagName)
    {
        public void OnCompleted() => OnCompleted(activity, awaiter, returnValueTagName);

        public static void OnCompleted(Activity activity, ObjectMethodExecutorAwaitable.Awaiter awaiter, string? returnValueTagName)
        {
            try
            {
                var result = awaiter.GetResult();

                if (returnValueTagName != null) activity.SetTagEnumerable(returnValueTagName, result);
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
