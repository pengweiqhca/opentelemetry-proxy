using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.StandardFiles;

namespace OpenTelemetry.StaticProxy.Fody.Tests.StandardTest;

public class NonActivityTest
{
    [Fact]
    public void ClassHaveActivitySource()
    {
        Assert.Null(TestHelper.GetProxyMethod<NonActivityTestClass1.TestClass1>(x => x.TestMethod1));

        Assert.IsType<SuppressInstrumentationMethod>(
            TestHelper.GetProxyMethod<NonActivityTestClass1.TestClass1>(x => x.TestMethod2));
    }

    [Fact]
    public void ClassHaveActivityName()
    {
        Assert.Null(TestHelper.GetProxyMethod<NonActivityTestClass2>(x => NonActivityTestClass2.TestMethod1));

        Assert.IsType<SuppressInstrumentationMethod>(
            TestHelper.GetProxyMethod<NonActivityTestClass2>(x => NonActivityTestClass2.TestMethod2));
    }

    [Fact]
    public void ClassHaveNoAttribute()
    {
        Assert.Null(TestHelper.GetProxyMethod<NonActivityTestClass3>(x => NonActivityTestClass2.TestMethod1));

        Assert.IsType<SuppressInstrumentationMethod>(
            TestHelper.GetProxyMethod<NonActivityTestClass3>(x => NonActivityTestClass2.TestMethod2));
    }
}
