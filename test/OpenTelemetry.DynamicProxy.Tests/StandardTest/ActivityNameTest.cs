using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.StandardFiles;

namespace OpenTelemetry.DynamicProxy.Tests.StandardTest;

public class ActivityNameTest
{
    [Fact]
    public void ActivityNameNoName()
    {
        var activityNameMethod = Assert.IsType<ActivityNameMethod>(TestHelper.GetProxyMethod<ActivityNameTestClass1>(x => x.TestMethod1));

        Assert.Equal("ActivityNameTestClass1.TestMethod1", activityNameMethod.ActivityName);
        Assert.Equal(3, activityNameMethod.MaxUsableTimes);

        activityNameMethod = Assert.IsType<ActivityNameMethod>(TestHelper.GetProxyMethod<ActivityNameTestClass1>(x => x.TestMethod2));

        Assert.Equal("ActivityNameTestClass1.TestMethod2", activityNameMethod.ActivityName);
        Assert.Equal(2, activityNameMethod.MaxUsableTimes);

        activityNameMethod = Assert.IsType<ActivityNameMethod>(TestHelper.GetProxyMethod<ActivityNameTestClass1>(x => x.TestMethod3));

        Assert.Equal("Test", activityNameMethod.ActivityName);
        Assert.Equal(4, activityNameMethod.MaxUsableTimes);
    }

    [Fact]
    public void ActivityNameHaveName()
    {
        var activityNameMethod = Assert.IsType<ActivityNameMethod>(TestHelper.GetProxyMethod<ActivityNameTestClass2>(x => x.TestMethod1));

        Assert.Equal("TestClass.TestMethod1", activityNameMethod.ActivityName);
        Assert.Equal(1, activityNameMethod.MaxUsableTimes);

        activityNameMethod = Assert.IsType<ActivityNameMethod>(TestHelper.GetProxyMethod<ActivityNameTestClass2>(x => x.TestMethod2));

        Assert.Equal("TestClass.TestMethod2", activityNameMethod.ActivityName);
        Assert.Equal(2, activityNameMethod.MaxUsableTimes);

        activityNameMethod = Assert.IsType<ActivityNameMethod>(TestHelper.GetProxyMethod<ActivityNameTestClass2>(x => x.TestMethod3));

        Assert.Equal("Test", activityNameMethod.ActivityName);
        Assert.Equal(4, activityNameMethod.MaxUsableTimes);
    }

    [Fact]
    public void TypeHaveNoActivityName()
    {
        Assert.Null(TestHelper.GetProxyMethod<ActivityNameTestClass3>(x => x.TestMethod1));

        var activityNameMethod = Assert.IsType<ActivityNameMethod>(TestHelper.GetProxyMethod<ActivityNameTestClass3>(x => x.TestMethod2));

        Assert.Equal("ActivityNameTestClass3.TestMethod2", activityNameMethod.ActivityName);
        Assert.Equal(2, activityNameMethod.MaxUsableTimes);

        activityNameMethod = Assert.IsType<ActivityNameMethod>(TestHelper.GetProxyMethod<ActivityNameTestClass3>(x => x.TestMethod3));

        Assert.Equal("Test", activityNameMethod.ActivityName);
        Assert.Equal(4, activityNameMethod.MaxUsableTimes);
    }

    [Fact]
    public void TypeHaveActivityNameAndActivitySource()
    {
        Assert.Null(TestHelper.GetProxyMethod<ActivityNameTestClass4>(x => x.TestMethod1));

        var activityNameMethod = Assert.IsType<ActivityNameMethod>(TestHelper.GetProxyMethod<ActivityNameTestClass4>(x => x.TestMethod2));

        Assert.Equal("ActivityNameTestClass4.TestMethod2", activityNameMethod.ActivityName);
        Assert.Equal(2, activityNameMethod.MaxUsableTimes);

        activityNameMethod = Assert.IsType<ActivityNameMethod>(TestHelper.GetProxyMethod<ActivityNameTestClass4>(x => x.TestMethod3));

        Assert.Equal("Test", activityNameMethod.ActivityName);
        Assert.Equal(4, activityNameMethod.MaxUsableTimes);
    }
}
