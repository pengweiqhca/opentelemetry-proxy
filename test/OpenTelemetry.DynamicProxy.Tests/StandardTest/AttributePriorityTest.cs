using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.StandardFiles;

namespace OpenTelemetry.DynamicProxy.Tests.StandardTest;

public class AttributePriorityTest
{
    [Fact]
    public void ActivitySourceTest()
    {
        Assert.Null(TestHelper.GetProxyMethod<AttributePriorityTestClass1>(x => x.TestMethod1));

        Assert.IsType<ActivityMethod>(TestHelper.GetProxyMethod<AttributePriorityTestClass1>(x => x.TestMethod2));
    }

    [Fact]
    public void ActivityNameTest()
    {
        Assert.Null(TestHelper.GetProxyMethod<AttributePriorityTestClass2>(x => x.TestMethod1));

        Assert.IsType<ActivityMethod>(TestHelper.GetProxyMethod<AttributePriorityTestClass2>(x => x.TestMethod2));
    }
}
