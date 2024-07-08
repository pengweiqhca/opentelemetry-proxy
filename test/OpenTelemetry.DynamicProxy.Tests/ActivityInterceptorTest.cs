using Castle.DynamicProxy;
using FluentAssertions;
using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.Tests.Common;
using System.Linq.Expressions;

namespace OpenTelemetry.DynamicProxy.Tests;

public class ActivityInterceptorTest : IDisposable
{
    private readonly ITestInterface _target = new ProxyGenerator()
        .CreateInterfaceProxyWithTarget<ITestInterface>(new TestInterface1(),
            new ActivityInterceptor(new ActivityInvokerFactory()));

    private readonly ITestInterface2 _target2 = new ProxyGenerator()
        .CreateInterfaceProxyWithTarget<ITestInterface2>(new TestInterface2(),
            new ActivityInterceptor(new ActivityInvokerFactory()));

    private readonly ActivityListener _listener = new();

    static ActivityInterceptorTest() => CompletionTrackingAwaiterBase.Initialize();

    public ActivityInterceptorTest() => ActivitySource.AddActivityListener(_listener);

    [Fact]
    public void GetActivityTags()
    {
        List<string> tags = ["_now", "Now", "e", Guid.NewGuid().ToString("N")];
        var invocation = Expression.Parameter(typeof(IInvocation), "invocation");

        var activityTags = ActivityInvokerFactory.GetActivityTags(typeof(TestClass3),
            typeof(TestClass3).GetMethod(nameof(TestClass3.InstanceMethod))!, tags, invocation,
            out var returnValueTagName);

        Assert.Equal("ghi", returnValueTagName);
        Assert.Equal(9, activityTags.Count);

        AssertNameAndPosition(activityTags[0], "_now", SetTagPosition.Start);
        AssertConvertExpression(AssertMemberExpression(activityTags[0].Value, "_now"));

        AssertNameAndPosition(activityTags[1], "abc", SetTagPosition.Start);
        AssertConvertExpression(AssertMemberExpression(activityTags[1].Value, "_now2"));

        AssertNameAndPosition(activityTags[2], "Now", SetTagPosition.Start);
        AssertConvertExpression(AssertMemberExpression(activityTags[2].Value, "Now"));

        AssertNameAndPosition(activityTags[3], "def", SetTagPosition.Start);
        Assert.Null(AssertMemberExpression(activityTags[3].Value, "Now2"));

        AssertNameAndPosition(activityTags[4], "a2", SetTagPosition.Start);
        AssertCallExpression(activityTags[4].Value, invocation, 0);

        AssertNameAndPosition(activityTags[5], "b", SetTagPosition.Start);
        AssertCallExpression(activityTags[5].Value, invocation, 2);

        AssertNameAndPosition(activityTags[6], "c", SetTagPosition.End);
        AssertCallExpression(activityTags[6].Value, invocation, 3);

        AssertNameAndPosition(activityTags[7], "d", SetTagPosition.All);
        AssertCallExpression(activityTags[7].Value, invocation, 4);

        AssertNameAndPosition(activityTags[8], "e", SetTagPosition.Start);
        AssertCallExpression(activityTags[8].Value, invocation, 5);

        static void AssertNameAndPosition(ActivityTag activityTag, string name, SetTagPosition position)
        {
            Assert.Equal(name, activityTag.Key);
            Assert.Equal(position, activityTag.Direction);
        }

        static void AssertConvertExpression(Expression? expression)
        {
            Assert.NotNull(expression);

            var unaryExpression = Assert.IsType<UnaryExpression>(expression);

            Assert.Equal(ExpressionType.Convert, unaryExpression.NodeType);

            Assert.Equal(typeof(TestClass3), unaryExpression.Type);

            AssertMemberExpression(unaryExpression.Operand, "InvocationTarget");
        }

        static Expression? AssertMemberExpression(Expression expression, string name)
        {
            var memberExpression = Assert.IsAssignableFrom<MemberExpression>(expression);

            Assert.Equal(ExpressionType.MemberAccess, memberExpression.NodeType);

            Assert.Equal(name, memberExpression.Member.Name);

            return memberExpression.Expression;
        }

        static void AssertCallExpression(Expression expression, Expression invocation, int index)
        {
            var methodCallExpression = Assert.IsAssignableFrom<MethodCallExpression>(expression);

            Assert.Equal(invocation, methodCallExpression.Object);

            Assert.Equal(typeof(IInvocation).GetMethod("GetArgumentValue"), methodCallExpression.Method);

            Assert.Equal(index, Assert.IsType<int>(Assert.IsType<ConstantExpression>(
                Assert.Single(methodCallExpression.Arguments)).Value));
        }
    }

    [Fact]
    public async Task FullTags()
    {
        var random = new Random();

        var now = DateTime.Now.AddDays(-1);
        var now2 = DateTime.Now.AddDays(1);

        var testClass3 = (TestClass3)new ProxyGenerator()
            .CreateClassProxy(typeof(TestClass3), [now, now2], new ActivityInterceptor(new ActivityInvokerFactory()));

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
            }, $"Test.{nameof(TestClass3.InstanceMethod)}", ActivityStatusCode.Unset, () => new()
            {
                { "_now", now },
                { "abc", now2 },
                { "Now", testClass3.Now },
                { "def", TestClass3.Now2 },
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
        $"{typeof(TestInterface1).FullName}.{nameof(ITestInterface.Method0)}",
        ActivityStatusCode.Unset);

    [Fact]
    public Task SyncMethodTest() => Intercept(target =>
        {
            target.Method1();

            return default;
        },
        $"{typeof(TestInterface1).FullName}.{nameof(ITestInterface.Method1)}",
        ActivityStatusCode.Unset, () => new() { { "abc", 1 } });

    [Fact]
    public Task TaskMethodTest() => Intercept(target => new(target.Method2()),
        $"{typeof(TestInterface1).FullName}.{nameof(ITestInterface.Method2)}",
        ActivityStatusCode.Unset);

    [Fact]
    public Task InterfaceHierarchyTest() => Intercept(_target2, target =>
        {
            var result = target.Method2();

            _target2.Dispose();

            return new(result);
        },
        $"{typeof(TestInterface2).FullName}.{nameof(ITestInterface.Method2)}",
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
        $"{typeof(TestInterface1).FullName}.{nameof(ITestInterface.Method3)}",
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
        $"{typeof(TestInterface1).FullName}.{nameof(ITestInterface.Method4)}",
        ActivityStatusCode.Error, () => new() { { "delay", 100 } });

    [Fact]
    public Task ValueTaskTTest() => Intercept(
        target => new(target.Method5(100).AsTask()),
        $"{typeof(TestInterface1).FullName}.{nameof(ITestInterface.Method5)}",
        ActivityStatusCode.Unset,
        () => new() { { "delay", 100 }, { "Field", "Abc" }, { ActivityTagAttribute.ReturnValueTagName, 100 } });

    [Fact]
    public Task CustomAwaitableTest() => Intercept(async target =>
        {
            try
            {
                await target.Method6(100);
            }
            catch (NotSupportedException) { }
        },
        $"{typeof(TestInterface1).FullName}.{nameof(ITestInterface.Method6)}",
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
