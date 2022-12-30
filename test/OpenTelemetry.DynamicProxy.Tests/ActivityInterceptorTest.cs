using Castle.DynamicProxy;

namespace OpenTelemetry.DynamicProxy.Tests;

public class ActivityInterceptorTest : IDisposable
{
    private readonly ITestInterface _target = new ProxyGenerator()
        .CreateInterfaceProxyWithTarget<ITestInterface>(new TestInterface1(),
            new ActivityInterceptor(new ActivityInvokerFactory()));
    private readonly ActivityListener _listener = new();

    public ActivityInterceptorTest() => ActivitySource.AddActivityListener(_listener);

    [Fact]
    public Task VoidMethodTest() => Intercept(target =>
        {
            target.Method0();

            return default;
        },
        $"{typeof(ITestInterface).FullName}.{nameof(ITestInterface.Method0)}",
        ActivityStatusCode.Unset);

    [Fact]
    public Task SyncMethodTest() => Intercept(target =>
        {
            target.Method1();

            return default;
        },
        $"{typeof(ITestInterface).FullName}.{nameof(ITestInterface.Method1)}",
        ActivityStatusCode.Unset);

    [Fact]
    public Task TaskMethodTest() => Intercept(target => new(target.Method2()),
        $"{typeof(ITestInterface).FullName}.{nameof(ITestInterface.Method2)}",
        ActivityStatusCode.Unset);

    [Fact]
    public Task TaskTMethodTest() => Intercept(async target =>
        {
            try
            {
                await target.Method3(100).ConfigureAwait(false);
            }
            catch (NotSupportedException) { }
        },
        $"{typeof(ITestInterface).FullName}.{nameof(ITestInterface.Method3)}",
        ActivityStatusCode.Error);

    [Fact]
    public Task ValueTaskTest() => Intercept(async target =>
        {
            try
            {
                await target.Method4(100).ConfigureAwait(false);
            }
            catch (NotSupportedException) { }
        },
        $"{typeof(ITestInterface).FullName}.{nameof(ITestInterface.Method4)}",
        ActivityStatusCode.Error);

    [Fact]
    public Task ValueTaskTTest() => Intercept(
        target => new(target.Method5(100).AsTask()),
        $"{typeof(ITestInterface).FullName}.{nameof(ITestInterface.Method5)}",
        ActivityStatusCode.Unset);

    [Fact]
    public Task CustomAwaitableTest() => Intercept(async target =>
        {
            try
            {
                await target.Method6(100);
            }
            catch (NotSupportedException) { }
        },
        $"{typeof(ITestInterface).FullName}.{nameof(ITestInterface.Method6)}",
        ActivityStatusCode.Error);

    private async Task Intercept(Func<ITestInterface, ValueTask> func, string name, ActivityStatusCode statusCode)
    {
        using var activity = new Activity("Test").Start();

        activity.ActivityTraceFlags |= ActivityTraceFlags.Recorded;

        using var listener = new ActivityListener();

        ActivitySource.AddActivityListener(listener);

        var tcs = new TaskCompletionSource<Activity>();

        listener.ActivityStopped += a => tcs.TrySetResult(a);
        listener.ShouldListenTo += _ => true;
        listener.Sample += delegate { return ActivitySamplingResult.AllDataAndRecorded; };

        await func(_target).ConfigureAwait(false);

        if (await Task.WhenAny(tcs.Task, Task.Delay(100)).ConfigureAwait(false) != tcs.Task)
            throw new TimeoutException();

        var child = await tcs.Task.ConfigureAwait(false);

        Assert.Equal(activity.TraceId, child.TraceId);
        Assert.Equal(name, child.OperationName);
        Assert.Equal(statusCode, child.Status);
    }

    public void Dispose() => _listener.Dispose();
}
