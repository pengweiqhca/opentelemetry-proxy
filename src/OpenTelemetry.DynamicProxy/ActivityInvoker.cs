using Microsoft.Extensions.Internal;
using OpenTelemetry.Proxy;
using OpenTelemetry.Trace;

namespace OpenTelemetry.DynamicProxy;

public class ActivityInvoker(
    ActivitySource activitySource,
    string activityName,
    ActivityKind kind,
    Tuple<Action<IInvocation, Activity>?, Action<IInvocation, Activity>?> setTags,
    string? returnValueTagName) : IActivityInvoker
{
    public void Invoke(IInvocation invocation)
    {
        if (activitySource.StartActivity(activityName, kind) is not { } activity)
        {
            invocation.Proceed();

            return;
        }

        setTags.Item1?.Invoke(invocation, activity);

        try
        {
            invocation.Proceed();
        }
        catch (Exception ex)
        {
            OnException(activity, ex);

            activity.Dispose();

            throw;
        }

        setTags.Item2?.Invoke(invocation, activity);

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

    private static void OnException(Activity activity, Exception ex) =>
        activity.SetStatus(ActivityStatusCode.Error, ex.Message).RecordException(ex);

    private sealed class ActivityAwaiter(
        Activity activity,
        ObjectMethodExecutorAwaitable.Awaiter awaiter,
        string? returnValueTagName)
    {
        public void OnCompleted() => OnCompleted(activity, awaiter, returnValueTagName);

        public static void OnCompleted(Activity activity, ObjectMethodExecutorAwaitable.Awaiter awaiter,
            string? returnValueTagName)
        {
            try
            {
                var result = awaiter.GetResult();

                if (returnValueTagName != null) activity.SetTagEnumerable(returnValueTagName, result);
            }
            catch (Exception ex)
            {
                OnException(activity, ex);
            }
            finally
            {
                activity.Dispose();
            }
        }
    }
}
