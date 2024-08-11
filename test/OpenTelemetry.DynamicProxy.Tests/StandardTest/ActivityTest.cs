using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.StandardFiles;

namespace OpenTelemetry.DynamicProxy.Tests.StandardTest;

public class ActivityTest
{
    [Fact]
    public void NotIncludeNonAsyncStateMachineMethod()
    {
        Assert.Null(TestHelper.GetProxyMethod<ActivityTestClass1>(x => x.TestMethod1));

        var activityContext = Assert.IsType<ActivityMethod>(TestHelper.GetProxyMethod<ActivityTestClass1>(x => x.TestMethod2));

        Assert.Equal("TestMethod", activityContext.ActivityName);
        Assert.True(activityContext.SuppressInstrumentation);
        Assert.Equal(ActivityKind.Server, activityContext.Kind);

        Assert.Null(TestHelper.GetProxyMethod<ActivityTestClass1>(x => x.TestMethod3));

        activityContext = Assert.IsType<ActivityMethod>(TestHelper.GetProxyMethod<ActivityTestClass1>(x => x.TestMethod4));

        Assert.Equal("ActivityTestClass.TestMethod4", activityContext.ActivityName);
        Assert.False(activityContext.SuppressInstrumentation);
        Assert.Equal(ActivityKind.Client, activityContext.Kind);

        Assert.Null(TestHelper.GetProxyMethod<ActivityTestClass1>(x => x.TestMethod5));
    }

    [Fact]
    public void IncludeNonAsyncStateMachineMethod()
    {
        var activityContext = Assert.IsType<ActivityMethod>(TestHelper.GetProxyMethod<ActivityTestClass2<int>>(x => x.TestMethod1));

        Assert.True(activityContext.SuppressInstrumentation);
        Assert.Equal("ActivityTestClass2`1.TestMethod1", activityContext.ActivityName);
        Assert.Equal(default, activityContext.Kind);
    }

    [Fact]
    public void TypeNoAttribute()
    {
        var activityContext = Assert.IsType<ActivityMethod>(TestHelper.GetProxyMethod<ActivityTestClass3>(x => x.TestMethod1));

        Assert.Equal("ActivityTestClass3.TestMethod1", activityContext.ActivityName);
    }
}
