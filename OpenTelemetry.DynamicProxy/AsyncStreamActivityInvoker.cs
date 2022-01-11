namespace OpenTelemetry.DynamicProxy;

internal class AsyncStreamActivityInvoker<TResult> : ActivityInvoker
{
    public AsyncStreamActivityInvoker(ActivitySource activitySource, string? activityName, ActivityKind kind)
        : base(activitySource, activityName, kind) { }

    protected override void InvokeAfter(IInvocation invocation, Activity activity) =>
        invocation.ReturnValue = Await((IAsyncEnumerable<TResult>)invocation.ReturnValue, activity);

    private static async IAsyncEnumerable<TResult> Await(IAsyncEnumerable<TResult> enumerable, Activity activity)
    {
        var enumerator = enumerable.GetAsyncEnumerator();

        while (true)
        {
            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) break;
            }
            catch (Exception ex)
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);

                OnException(activity, ex);

                throw;
            }

            yield return enumerator.Current;
        }

        await enumerator.DisposeAsync().ConfigureAwait(false);

        activity.Stop();
    }
}

internal class AsyncStreamActivityNameInvoker<TResult> : ActivityNameInvoker
{
    public AsyncStreamActivityNameInvoker(string? activityName, int maxUseableTimes, bool suppressInstrumentation)
        : base(activityName, maxUseableTimes, suppressInstrumentation) { }

    protected override void InvokeAfter(IInvocation invocation, IDisposable? disposable) =>
        invocation.ReturnValue = Await((IAsyncEnumerable<TResult>)invocation.ReturnValue, invocation, disposable);

    private async IAsyncEnumerable<TResult> Await(IAsyncEnumerable<TResult> enumerable, IInvocation invocation, IDisposable? disposable)
    {
        var enumerator = enumerable.GetAsyncEnumerator();

        while (true)
        {
            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) break;
            }
            catch
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);

                base.InvokeAfter(invocation, disposable);

                throw;
            }

            yield return enumerator.Current;
        }

        await enumerator.DisposeAsync().ConfigureAwait(false);

        base.InvokeAfter(invocation, disposable);
    }
}
