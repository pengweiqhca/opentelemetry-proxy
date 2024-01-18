using Microsoft.Extensions.Internal;
using OpenTelemetry.Proxy;
using OpenTelemetry.Trace;

namespace OpenTelemetry.DynamicProxy;

public class ActivityInvoker : IActivityInvoker
{
    private readonly ActivitySource _activitySource;
    private readonly string _activityName;
    private readonly ActivityKind _kind;
    private readonly Action<IInvocation, Activity>? _setTags;
    private readonly string? _returnValueTagName;

    public ActivityInvoker(ActivitySource activitySource, string activityName, ActivityKind kind,
        Action<IInvocation, Activity>? setTags, string? returnValueTagName)
    {
        _activitySource = activitySource;
        _activityName = activityName;
        _kind = kind;
        _setTags = setTags;
        _returnValueTagName = returnValueTagName;
    }

    public void Invoke(IInvocation invocation)
    {
        if (_activitySource.StartActivity(_activityName, _kind) is not { } activity)
        {
            invocation.Proceed();

            return;
        }

        _setTags?.Invoke(invocation, activity);

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
            if (_returnValueTagName != null) activity.SetTagEnumerable(_returnValueTagName, invocation.ReturnValue);

            activity.Dispose();

            return;
        }

        var awaiter = func(invocation.ReturnValue).GetAwaiter();
        if (awaiter.IsCompleted) ActivityAwaiter.OnCompleted(activity, awaiter, _returnValueTagName);
        else awaiter.UnsafeOnCompleted(new ActivityAwaiter(activity, awaiter, _returnValueTagName).OnCompleted);
    }

    private static void OnException(Activity activity, Exception ex) =>
        activity.SetStatus(ActivityStatusCode.Error, ex.Message).RecordException(ex);

    private sealed class ActivityAwaiter
    {
        private readonly Activity _activity;
        private readonly ObjectMethodExecutorAwaitable.Awaiter _awaiter;
        private readonly string? _returnValueTagName;

        public ActivityAwaiter(Activity activity, ObjectMethodExecutorAwaitable.Awaiter awaiter,
            string? returnValueTagName)
        {
            _activity = activity;
            _awaiter = awaiter;
            _returnValueTagName = returnValueTagName;
        }

        public void OnCompleted() => OnCompleted(_activity, _awaiter, _returnValueTagName);

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
