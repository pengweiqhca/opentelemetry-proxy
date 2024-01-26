using OpenTelemetry.Proxy;

namespace OpenTelemetry.DynamicProxy.Tests;

public class GetActivityNameTest
{
    [Fact]
    public void No_ActivityAttribute_No_ActivitySourceAttribute() =>
        Assert.Equal(ActivitySettings.None, ActivityInvokerHelper.GetActivityName(
            new Action(new GetActivityNameTest().No_ActivityAttribute_No_ActivitySourceAttribute).Method,
            typeof(GetActivityNameTest), out _, out _, out _));

    [Fact]
    public void No_ActivityAttribute_Has_ActivitySourceAttribute()
    {
        Assert.Equal(ActivitySettings.None,
            ActivityInvokerHelper.GetActivityName(new Action(new TestClass1().Method1).Method,
                typeof(TestClass1), out var activityName, out var kind, out _));

        Assert.Equal(ActivitySettings.None,
            ActivityInvokerHelper.GetActivityName(new Func<Task>(new TestClass1().MethodAsync1).Method,
                typeof(TestClass1), out activityName, out kind, out _));

        Assert.Equal(ActivitySettings.Activity,
            ActivityInvokerHelper.GetActivityName(new Func<Task>(new TestClass1().MethodAsyncWithStateMachine1).Method,
                typeof(TestClass1), out activityName, out kind, out _));

        Assert.Null(activityName);
        Assert.Equal(ActivityKind.Internal, kind);
    }

    [Fact]
    public void Has_ActivityAttribute()
    {
        Assert.Equal(ActivitySettings.Activity,
            ActivityInvokerHelper.GetActivityName(new Action(new TestClass1().Method2).Method,
                typeof(TestClass1), out var activityName, out var kind, out _));

        Assert.Equal("TestMethod2", activityName);
        Assert.Equal(ActivityKind.Client, kind);
    }

    [Fact]
    public void NoActivityAttribute() =>
        Assert.Equal(ActivitySettings.SuppressInstrumentation,
            ActivityInvokerHelper.GetActivityName(new Action(new TestClass1().Method3).Method,
                typeof(TestClass1), out _, out _, out _));

    [Fact, ActivityName(MaxUsableTimes = 3)]
    public void ActivityNameAttribute()
    {
        Assert.Equal(ActivitySettings.ActivityName, ActivityInvokerHelper.GetActivityName(
            new Action(new GetActivityNameTest().ActivityNameAttribute).Method,
            typeof(GetActivityNameTest), out var activityName, out _, out var maxUsableTimes));

        Assert.Null(activityName);

        Assert.Equal(3, maxUsableTimes);
    }

    [Fact, ActivityName(MaxUsableTimes = 0)]
    public void ActivityNameAttribute_MaxUsableTimes0() => Assert.Equal(ActivitySettings.None,
        ActivityInvokerHelper.GetActivityName(
            new Action(new GetActivityNameTest().ActivityNameAttribute_MaxUsableTimes0).Method,
            typeof(GetActivityNameTest), out _, out _, out _));

    [Fact]
    public void Interface_Default_AsyncMethod()
    {
        Assert.Equal(ActivitySettings.None, ActivityInvokerHelper.GetActivityName(
            typeof(ITestInterface2).GetMethod(nameof(ITestInterface2.SyncMethod))!,
            typeof(ITestInterface2), out _, out _, out _));

        Assert.Equal(ActivitySettings.Activity, ActivityInvokerHelper.GetActivityName(
            typeof(ITestInterface2).GetMethod(nameof(ITestInterface2.AsyncMethod))!,
            typeof(ITestInterface2), out _, out _, out _));
    }

    [ActivitySource]
    private interface ITestInterface2
    {
        void SyncMethod();

        Task AsyncMethod();
    }
}
