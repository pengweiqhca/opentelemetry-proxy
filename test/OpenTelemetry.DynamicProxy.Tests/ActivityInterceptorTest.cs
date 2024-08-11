using Castle.DynamicProxy;
using FluentAssertions;
using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.StandardFiles;
using OpenTelemetry.Proxy.Tests.Common;

namespace OpenTelemetry.DynamicProxy.Tests;

public class ActivityInterceptorTest : IDisposable
{
    private readonly ITestInterface _target = new ProxyGenerator()
        .CreateInterfaceProxyWithTargetInterface<ITestInterface>(new TestInterface1(),
            new ActivityInterceptor(new ActivityInvokerFactory()));

    private readonly ITestInterface2 _target2 = new ProxyGenerator()
        .CreateInterfaceProxyWithTarget<ITestInterface2>(new TestInterface2(),
            new ActivityInterceptor(new ActivityInvokerFactory()));

    private readonly ActivityListener _listener = new();

    static ActivityInterceptorTest() => CompletionTrackingAwaiterBase.Initialize();

    public ActivityInterceptorTest() => ActivitySource.AddActivityListener(_listener);

    [Fact]
    public async Task FullTags()
    {
        var random = new Random();

        var now = DateTime.Now.AddDays(-1);
        var now2 = DateTime.Now.AddDays(1);

        var testClass3 = (ActivityTagTestClass2)new ProxyGenerator()
            .CreateClassProxy(typeof(ActivityTagTestClass2), [now, now2], new ActivityInterceptor(new ActivityInvokerFactory()));

        var a = random.Next();
        var a2 = random.Next();
        var b = random.Next();
        var c = DateTimeOffset.MinValue;
        var d = random.Next();
        var d2 = d;
        var e = random.Next();

        await Intercept(testClass3, target =>
            {
                target.InstanceMethod(a, a2, b, out c, ref d, e);

                return default;
            }, $"Test.{nameof(ActivityTagTestClass2.InstanceMethod)}", ActivityStatusCode.Unset, () => new()
            {
                { "_now", now },
                { "Now", testClass3.Now },
                { "a2", a },
                { "b", b },
                { "c", c },
                { "d", d2 },
                { "d$out", d },
                { "e", e },
                { "ghi", a2 },
            }).ConfigureAwait(false);
    }

    [Fact]
    public Task VoidMethodTest() => Intercept(target =>
        {
            target.Method0();

            return default;
        },
        $"{nameof(ITestInterface)}.{nameof(ITestInterface.Method0)}",
        ActivityStatusCode.Unset);

    [Fact]
    public Task SyncMethodTest() => Intercept(target =>
        {
            target.Method1();

            return default;
        },
        $"{nameof(ITestInterface)}.{nameof(TestInterface1.Method1)}",
        ActivityStatusCode.Unset, () => new() { { "abc", 1 } });

    [Fact]
    public Task TaskMethodTest() => Intercept(target => new(target.Method2()),
        $"{nameof(TestInterface1)}.{nameof(TestInterface1.Method2)}",
        ActivityStatusCode.Unset);

    [Fact]
    public Task InterfaceHierarchyTest() => Intercept(_target2, target =>
        {
            var result = target.Method2();

            _target2.Dispose();

            return new(result);
        },
        $"{nameof(TestInterface1)}.{nameof(TestInterface1.Method2)}",
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
        $"{nameof(ITestInterface)}.{nameof(ITestInterface.Method3)}",
        ActivityStatusCode.Error, () => new() { { "delay", 100 } });

    [Fact]
    public Task ValueTaskTest() => Intercept(async target =>
        {
            try
            {
                await target.Method4(100).ConfigureAwait(false);
            }
            catch (NotSupportedException) { }
        },
        $"{nameof(ITestInterface)}.{nameof(TestInterface1.Method4)}",
        ActivityStatusCode.Error, () => new() { { "delay", 100 } });

    [Fact]
    public Task ValueTaskTTest() => Intercept(
        target => new(target.Method5(100).AsTask()),
        $"{nameof(ITestInterface)}.{nameof(ITestInterface.Method5)}",
        ActivityStatusCode.Unset,
        () => new() { { "delay", 100 }, { "$returnvalue", 100 } });

    [Fact]
    public Task CustomAwaitableTest() => Intercept(async target =>
        {
            try
            {
                await target.Method6(100);
            }
            catch (NotSupportedException) { }
        },
        $"{nameof(ITestInterface)}.{nameof(TestInterface1.Method6)}",
        ActivityStatusCode.Error, () => new() { { "delay", 100 }, { "Now", new DateTime(2024, 1, 1) } });

    private Task Intercept(Func<ITestInterface, ValueTask> func, string name, ActivityStatusCode statusCode,
        Func<Dictionary<string, object>>? getTags = null) => Intercept(_target, func, name, statusCode, getTags);

    private static async Task Intercept<T>(T target, Func<T, ValueTask> func, string name,
        ActivityStatusCode statusCode, Func<Dictionary<string, object>>? getTags = null)
    {
        using var activity = new Activity("Test").Start();

        activity.ActivityTraceFlags |= ActivityTraceFlags.Recorded;

        using var listener = new ActivityListener();

        ActivitySource.AddActivityListener(listener);

        var tcs = new TaskCompletionSource<Activity>();

        listener.ActivityStopped += a => tcs.TrySetResult(a);
        listener.ShouldListenTo += _ => true;
        listener.Sample += delegate { return ActivitySamplingResult.AllDataAndRecorded; };

        await func(target).ConfigureAwait(false);

        if (!Debugger.IsAttached && await Task.WhenAny(tcs.Task, Task.Delay(100)).ConfigureAwait(false) != tcs.Task)
            throw new TimeoutException();

        var child = await tcs.Task.ConfigureAwait(false);

        Assert.Equal(activity.TraceId, child.TraceId);
        Assert.Equal(name, child.OperationName);
        Assert.Equal(statusCode, child.Status);

        if (getTags != null)
            foreach (var kv in getTags())
                child.GetTagItem(kv.Key).Should().Be(kv.Value, "Activity tag `{0}` should be equal", kv.Key);
    }

    [Fact]
    public void SuppressInstrumentationTest()
    {
        Assert.False(Sdk.SuppressInstrumentation);

        Assert.True(_target.Method7());

        Assert.False(Sdk.SuppressInstrumentation);
    }

    public void Dispose() => _listener.Dispose();
}
