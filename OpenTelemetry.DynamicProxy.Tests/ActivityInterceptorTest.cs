using OpenTelemetry.Trace;

namespace OpenTelemetry.DynamicProxy.Tests;

public class ActivityInterceptorTest : IDisposable
{
    private readonly ITestInterface _target = new ProxyGenerator()
        .CreateInterfaceProxyWithTarget<ITestInterface>(new TestInterface1(), new ActivityInterceptor(new ActivityInvokerFactory()));
    private readonly ActivityListener _listener = new();

    public ActivityInterceptorTest() => ActivitySource.AddActivityListener(_listener);

    [Theory]
    [MemberData(nameof(InterceptData))]
    public async Task Intercept(Func<ITestInterface, ValueTask> func, string name, StatusCode statusCode)
    {
        using var activity = new Activity("Test").Start();

        activity.ActivityTraceFlags |= ActivityTraceFlags.Recorded;

        using var listener = new ActivityListener();

        ActivitySource.AddActivityListener(listener);

        var list = new List<Activity>();

        listener.ActivityStarted += list.Add;
        listener.ShouldListenTo += _ => true;
        listener.Sample += delegate { return ActivitySamplingResult.AllDataAndRecorded; };

        await func(_target).ConfigureAwait(false);

        Assert.Single(list);

        Assert.Equal(name, list[0].OperationName);
        Assert.Equal(statusCode, list[0].GetStatus().StatusCode);
    }

    public static IEnumerable<object[]> InterceptData()
    {
        yield return new object[]
        {
            new Func<ITestInterface, ValueTask>(target =>
            {
                target.Method0();

                return default;
            }),
            $"{typeof(ITestInterface).FullName}.{nameof(ITestInterface.Method0)}",
            StatusCode.Ok,
        };

        yield return new object[]
        {
            new Func<ITestInterface, ValueTask>(target =>
            {
                target.Method1();

                return default;
            }),
            $"{typeof(ITestInterface).FullName}.{nameof(ITestInterface.Method1)}",
            StatusCode.Ok,
        };

        yield return new object[]
        {
            new Func<ITestInterface, ValueTask>(target => new (target.Method2())),
            $"{typeof(ITestInterface).FullName}.{nameof(ITestInterface.Method2)}",
            StatusCode.Ok,
        };

        yield return new object[]
        {
            new Func<ITestInterface, ValueTask>(async target =>
            {
                try
                {
                    await target.Method3(100).ConfigureAwait(false);
                }
                catch (NotSupportedException) { }
            }),
            $"{typeof(ITestInterface).FullName}.{nameof(ITestInterface.Method3)}",
            StatusCode.Error,
        };

        yield return new object[]
        {
            new Func<ITestInterface, ValueTask>(async  target =>
            {
                try
                {
                    await target.Method4().ConfigureAwait(false);
                }
                catch (NotSupportedException) { }
            }),
            $"{typeof(ITestInterface).FullName}.{nameof(ITestInterface.Method4)}",
            StatusCode.Error,
        };

        yield return new object[]
        {
            new Func<ITestInterface, ValueTask>(target => new (target.Method5().AsTask())),
            $"{typeof(ITestInterface).FullName}.{nameof(ITestInterface.Method5)}",
            StatusCode.Ok,
        };

        yield return new object[]
        {
            new Func<ITestInterface, ValueTask>(target => new (target.Method6(100).ToListAsync().AsTask())),
            $"{typeof(ITestInterface).FullName}.{nameof(ITestInterface.Method6)}",
            StatusCode.Ok,
        };
    }

    public void Dispose() => _listener.Dispose();
}
