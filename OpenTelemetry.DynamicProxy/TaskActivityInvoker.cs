using System;

namespace OpenTelemetry.DynamicProxy;

internal class TaskActivityInvoker : ActivityInvoker
{
    public TaskActivityInvoker(ActivitySource activitySource, string? activityName, ActivityKind kind)
        : base(activitySource, activityName, kind) { }

    protected override void InvokeAfter(IInvocation invocation, Activity activity) =>
        invocation.ReturnValue = Await((Task)invocation.ReturnValue, activity);

    private static async Task Await(Task task, Activity activity)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnException(activity, ex);

            throw;
        }

        Stop(activity);
    }
}

internal class TaskActivityNameInvoker : ActivityNameInvoker
{
    public TaskActivityNameInvoker(string? activityName, int maxUseableTimes, bool suppressInstrumentation)
        : base(activityName, maxUseableTimes, suppressInstrumentation) { }

    protected override void InvokeAfter(IInvocation invocation, IDisposable? disposable) =>
        invocation.ReturnValue = Await((Task)invocation.ReturnValue, invocation, disposable);

    private async Task Await(Task task, IInvocation invocation, IDisposable? disposable)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            base.InvokeAfter(invocation, disposable);
        }
    }
}

internal class TaskActivityInvoker<TResult> : ActivityInvoker
{
    public TaskActivityInvoker(ActivitySource activitySource, string? activityName, ActivityKind kind)
        : base(activitySource, activityName, kind) { }

    protected override void InvokeAfter(IInvocation invocation, Activity activity) =>
        invocation.ReturnValue = Await((Task<TResult>)invocation.ReturnValue, activity);

    private static async Task<TResult> Await(Task<TResult> task, Activity activity)
    {
        TResult result;

        try
        {
            result = await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnException(activity, ex);

            throw;
        }

        Stop(activity);

        return result;
    }
}

internal class TaskActivityNameInvoker<TResult> : ActivityNameInvoker
{
    public TaskActivityNameInvoker(string? activityName, int maxUseableTimes, bool suppressInstrumentation)
        : base(activityName, maxUseableTimes, suppressInstrumentation) { }

    protected override void InvokeAfter(IInvocation invocation, IDisposable? disposable) =>
        invocation.ReturnValue = Await((Task<TResult>)invocation.ReturnValue, invocation, disposable);

    private async Task<TResult> Await(Task<TResult> task, IInvocation invocation, IDisposable? disposable)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            base.InvokeAfter(invocation, disposable);
        }
    }
}
