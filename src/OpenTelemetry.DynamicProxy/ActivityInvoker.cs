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

            Activity.Current = activity.Parent;
        }
        catch (Exception ex)
        {
            OnException(activity, ex);

            activity.Dispose();

            throw;
        }

        var func = ActivityInvokerHelper.Convert(invocation.Method.ReturnType);
        if (func == null)
        {
            if (returnValueTagName != null) activity.SetTagEnumerable(returnValueTagName, invocation.ReturnValue);

            setTags.Item2?.Invoke(invocation, activity);

            activity.Dispose();

            return;
        }

        var awaiter = func(invocation.ReturnValue).GetAwaiter();
        if (awaiter.IsCompleted)
            ActivityAwaiter.OnCompleted(activity, awaiter, invocation, setTags.Item2, returnValueTagName);
        else
            awaiter.UnsafeOnCompleted(
                new ActivityAwaiter(activity, awaiter, invocation, setTags.Item2, returnValueTagName).OnCompleted);
    }

    private static void OnException(Activity activity, Exception ex) =>
        activity.SetStatus(ActivityStatusCode.Error, ex.Message).RecordException(ex);

    private sealed class ActivityAwaiter(
        Activity activity,
        ObjectMethodExecutorAwaitable.Awaiter awaiter,
        IInvocation invocation,
        Action<IInvocation, Activity>? setTags,
        string? returnValueTagName)
    {
        public void OnCompleted() => OnCompleted(activity, awaiter, invocation, setTags, returnValueTagName);

        public static void OnCompleted(Activity activity, ObjectMethodExecutorAwaitable.Awaiter awaiter,
            IInvocation invocation, Action<IInvocation, Activity>? setTags, string? returnValueTagName)
        {
            try
            {
                var result = awaiter.GetResult();

                setTags?.Invoke(invocation, activity);

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
