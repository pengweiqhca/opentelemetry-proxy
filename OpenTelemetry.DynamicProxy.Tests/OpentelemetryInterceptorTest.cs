using OpenTelemetry.Trace;

namespace OpenTelemetry.DynamicProxy.Tests;

public class OpenTelemetryInterceptorTest : IDisposable
{
    private readonly ITestInterface1 _target = new ProxyGenerator()
        .CreateInterfaceProxyWithTarget<ITestInterface1>(new TestInterface1(), new OpenTelemetryInterceptor(new ActivityInvokerFactory()));
    private readonly ActivityListener _listener = new();

    public OpenTelemetryInterceptorTest() => ActivitySource.AddActivityListener(_listener);

    [Theory]
    [MemberData(nameof(InterceptData))]
    public async Task Intercept(Func<ITestInterface1, ValueTask> func, string name, StatusCode statusCode)
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
            new Func<ITestInterface1, ValueTask>(target =>
            {
                target.Method0();

                return default;
            }),
            $"{typeof(ITestInterface1).FullName}.{nameof(ITestInterface1.Method0)}",
            StatusCode.Ok,
        };

        yield return new object[]
        {
            new Func<ITestInterface1, ValueTask>(target =>
            {
                target.Method1();

                return default;
            }),
            $"{typeof(ITestInterface1).FullName}.{nameof(ITestInterface1.Method1)}",
            StatusCode.Ok,
        };

        yield return new object[]
        {
            new Func<ITestInterface1, ValueTask>(target => new (target.Method2())),
            $"{typeof(ITestInterface1).FullName}.{nameof(ITestInterface1.Method2)}",
            StatusCode.Ok,
        };

        yield return new object[]
        {
            new Func<ITestInterface1, ValueTask>(async target =>
            {
                try
                {
                    await target.Method3(100).ConfigureAwait(false);
                }
                catch (NotSupportedException) { }
            }),
            $"{typeof(ITestInterface1).FullName}.{nameof(ITestInterface1.Method3)}",
            StatusCode.Error,
        };

        yield return new object[]
        {
            new Func<ITestInterface1, ValueTask>(async  target =>
            {
                try
                {
                    await target.Method4().ConfigureAwait(false);
                }
                catch (NotSupportedException) { }
            }),
            $"{typeof(ITestInterface1).FullName}.{nameof(ITestInterface1.Method4)}",
            StatusCode.Error,
        };

        yield return new object[]
        {
            new Func<ITestInterface1, ValueTask>(target => new (target.Method5().AsTask())),
            $"{typeof(ITestInterface1).FullName}.{nameof(ITestInterface1.Method5)}",
            StatusCode.Ok,
        };

        yield return new object[]
        {
            new Func<ITestInterface1, ValueTask>(target => new (target.Method6(100).ToListAsync().AsTask())),
            $"{typeof(ITestInterface1).FullName}.{nameof(ITestInterface1.Method6)}",
            StatusCode.Ok,
        };
    }

    public void Dispose() => _listener.Dispose();
}
