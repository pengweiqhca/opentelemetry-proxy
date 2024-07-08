using Microsoft.Extensions.Internal;
using OpenTelemetry.Proxy;
using OpenTelemetry.Trace;

namespace OpenTelemetry.DynamicProxy;

public class ActivityInvoker(
    ActivitySource activitySource,
    string activityName,
    ActivityKind kind,
    bool suppressInstrumentation,
    Tuple<Action<IInvocation, Activity>?, Action<IInvocation, Activity>?> setTags,
    string? returnValueTagName) : ActivityNameInvoker
{
    public override void Invoke(IInvocation invocation)
    {
        if (activitySource.StartActivity(activityName, kind) is not { } activity)
        {
            if (suppressInstrumentation) base.Invoke(invocation);
            else invocation.Proceed();

            return;
        }

        setTags.Item1?.Invoke(invocation, activity);

        var disposable = suppressInstrumentation ? SuppressInstrumentationScope.Begin() : null;

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

        if (awaiter.IsCompleted) ActivityAwaiter.OnCompleted(activity, awaiter, disposable, returnValueTagName);
        else
        {
            Activity.Current = activity.Parent;

            awaiter.UnsafeOnCompleted(new ActivityAwaiter(activity, awaiter, disposable, returnValueTagName).OnCompleted);
        }
    }

    private static void OnException(Activity activity, Exception ex) =>
        activity.SetStatus(ActivityStatusCode.Error, ex.Message).RecordException(ex);

    private sealed class ActivityAwaiter(
        Activity activity,
        ObjectMethodExecutorAwaitable.Awaiter awaiter,
        IDisposable? disposable,
        string? returnValueTagName)
    {
        public void OnCompleted() => OnCompleted(activity, awaiter, disposable, returnValueTagName);

        public static void OnCompleted(Activity activity, ObjectMethodExecutorAwaitable.Awaiter awaiter,
            IDisposable? disposable, string? returnValueTagName)
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
                disposable?.Dispose();
                activity.Dispose();
            }
        }
    }
}
