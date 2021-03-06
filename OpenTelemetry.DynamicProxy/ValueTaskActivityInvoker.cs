namespace OpenTelemetry.DynamicProxy;

internal class ValueTaskActivityInvoker : ActivityInvoker
{
    public ValueTaskActivityInvoker(ActivitySource activitySource, string? activityName, ActivityKind kind)
        : base(activitySource, activityName, kind) { }

    protected override void InvokeAfter(IInvocation invocation, Activity activity) =>
        invocation.ReturnValue = Await((ValueTask)invocation.ReturnValue, activity);

    private static async ValueTask Await(ValueTask task, Activity activity)
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

internal class ValueTaskActivityNameInvoker : ActivityNameInvoker
{
    public ValueTaskActivityNameInvoker(string? activityName, int maxUseableTimes, bool suppressInstrumentation)
        : base(activityName, maxUseableTimes, suppressInstrumentation) { }

    protected override void InvokeAfter(IInvocation invocation, IDisposable? disposable) =>
        invocation.ReturnValue = Await((ValueTask)invocation.ReturnValue, invocation, disposable);

    private async ValueTask Await(ValueTask task, IInvocation invocation, IDisposable? disposable)
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

internal class ValueTaskActivityInvoker<TResult> : ActivityInvoker
{
    public ValueTaskActivityInvoker(ActivitySource activitySource, string? activityName, ActivityKind kind)
        : base(activitySource, activityName, kind) { }

    protected override void InvokeAfter(IInvocation invocation, Activity activity) =>
        invocation.ReturnValue = Await((ValueTask<TResult>)invocation.ReturnValue, activity);

    private static async ValueTask<TResult> Await(ValueTask<TResult> task, Activity activity)
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

internal class ValueTaskActivityNameInvoker<TResult> : ActivityNameInvoker
{
    public ValueTaskActivityNameInvoker(string? activityName, int maxUseableTimes, bool suppressInstrumentation)
        : base(activityName, maxUseableTimes, suppressInstrumentation) { }

    protected override void InvokeAfter(IInvocation invocation, IDisposable? disposable) =>
        invocation.ReturnValue = Await((ValueTask<TResult>)invocation.ReturnValue, invocation, disposable);

    private async ValueTask<TResult> Await(ValueTask<TResult> task, IInvocation invocation, IDisposable? disposable)
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
