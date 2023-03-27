using OpenTelemetry.Proxy;

namespace OpenTelemetry.DynamicProxy.Tests;

public class TryGetActivityNameTest
{
    [Fact]
    public void No_ActivityAttribute_No_ActivitySourceAttribute() =>
        Assert.Equal(ActivitySettings.NonActivity, ActivityInvokerHelper.GetActivityName(
            new Action(new TryGetActivityNameTest().No_ActivityAttribute_No_ActivitySourceAttribute).Method,
            typeof(TryGetActivityNameTest), out _, out _, out _));

    [Fact]
    public void No_ActivityAttribute_Has_ActivitySourceAttribute()
    {
        Assert.Equal(ActivitySettings.NonActivity,
            ActivityInvokerHelper.GetActivityName(new Action(new TestClass1().Method1).Method,
                typeof(TestClass1), out var activityName, out var kind, out _));

        Assert.Equal(ActivitySettings.NonActivity,
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
        Assert.Equal(ActivitySettings.NonActivityAndSuppressInstrumentation,
            ActivityInvokerHelper.GetActivityName(new Action(new TestClass1().Method3).Method,
            typeof(TestClass1), out _, out _, out _));

    [Fact, ActivityName(MaxUsableTimes = 3)]
    public void ActivityNameAttribute()
    {
        Assert.Equal(ActivitySettings.ActivityNameOnly, ActivityInvokerHelper.GetActivityName(
            new Action(new TryGetActivityNameTest().ActivityNameAttribute).Method,
            typeof(TryGetActivityNameTest), out var activityName, out _, out var maxUsableTimes));

        Assert.Null(activityName);

        Assert.Equal(3, maxUsableTimes);
    }

    [Fact, ActivityName(MaxUsableTimes = 0)]
    public void ActivityNameAttribute_MaxUsableTimes0() => Assert.Equal(ActivitySettings.NonActivity,
        ActivityInvokerHelper.GetActivityName(
            new Action(new TryGetActivityNameTest().ActivityNameAttribute_MaxUsableTimes0).Method,
            typeof(TryGetActivityNameTest), out _, out _, out _));
}
