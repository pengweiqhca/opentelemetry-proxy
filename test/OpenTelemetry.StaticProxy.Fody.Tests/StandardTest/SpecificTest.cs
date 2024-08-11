using OpenTelemetry.Proxy;
using OpenTelemetry.Proxy.StandardFiles;

namespace OpenTelemetry.StaticProxy.Fody.Tests.StandardTest;

public class SpecificTest
{
    [Fact]
    public void InterfaceTest()
    {
        var proxyType = TestHelper.GetProxyType<ITestInterface>();

        Assert.Equal(2, proxyType.Methods.Count);
    }

    [Fact]
    public void LocalFunctionTest()
    {
        var proxyType = TestHelper.GetProxyType<LocalFunctionTestClass>();

        Assert.Equal("LocalFunctionTestClass.Test+InlineMethod",
            Assert.IsType<ActivityMethod>(Assert.Single(proxyType.Methods.Values)).ActivityName);
    }
}
