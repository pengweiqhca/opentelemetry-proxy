namespace OpenTelemetry.DynamicProxy.Tests;

public class TryGetActivityNameTest
{
    [Fact]
    public void No_ActivityAttribute_No_ActivitySourceAttribute() =>
        Assert.False(ActivityInvokerHelper.TryGetActivityName(new Action(new TryGetActivityNameTest().No_ActivityAttribute_No_ActivitySourceAttribute).Method,
            typeof(TryGetActivityNameTest), out _, out _));

    [Fact]
    public void No_ActivityAttribute_Has_ActivitySourceAttribute()
    {
        Assert.True(ActivityInvokerHelper.TryGetActivityName(new Action(new TestClass1().Method1).Method,
            typeof(TestClass1), out var activityName, out var kind));

        Assert.Null(activityName);
        Assert.Equal(ActivityKind.Internal, kind);
    }

    [Fact]
    public void Has_ActivityAttribute()
    {
        Assert.True(ActivityInvokerHelper.TryGetActivityName(new Action(new TestClass1().Method2).Method,
            typeof(TestClass1), out var activityName, out var kind));

        Assert.Equal("TestMethod2", activityName);
        Assert.Equal(ActivityKind.Client, kind);
    }

    [Fact]
    public void NoActivityAttribute() =>
        Assert.False(ActivityInvokerHelper.TryGetActivityName(new Action(new TestClass1().Method3).Method,
            typeof(TestClass1), out _, out _));
}
