using OpenTelemetry.StaticProxy;

namespace OpenTelemetry.StaticProxy.Tests.StandardTest;

public class NonActivityTest
{
    [Fact]
    public async Task ClassHaveActivitySource()
    {
        var test = new ProxyRewriterTest("NonActivityTestClass1");

        var results = await test.VisitAsync().ConfigureAwait(false);

        Assert.Equal(2, results.Count);

        Assert.IsType<NoAttributeTypeContext>(results[0].Context);
        Assert.Equal("NonActivityTestClass1", results[0].TypeName);
        Assert.Equal("OpenTelemetry.Proxy.StandardFiles.NonActivityTestClass1", results[0].TypeFullName);
        Assert.Empty(results[0].MethodContexts);

        Assert.IsType<ActivitySourceContext>(results[1].Context);
        Assert.Equal("TestClass1", results[1].TypeName);
        Assert.Equal("OpenTelemetry.Proxy.StandardFiles.NonActivityTestClass1+TestClass1", results[1].TypeFullName);

        Assert.IsType<SuppressInstrumentationContext>(Assert.Single(results[1].MethodContexts.Values));
    }

    [Fact]
    public async Task ClassHaveActivityName()
    {
        var test = new ProxyRewriterTest("NonActivityTestClass2");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        Assert.IsType<TypeActivityNameContext>(typeMethods.Context);

        Assert.Equal("NonActivityTestClass2", typeMethods.TypeName);
        Assert.Equal("OpenTelemetry.Proxy.StandardFiles.NonActivityTestClass2", typeMethods.TypeFullName);

        Assert.IsType<SuppressInstrumentationContext>(Assert.Single(typeMethods.MethodContexts.Values));
    }

    [Fact]
    public async Task ClassHaveNoAttribute()
    {
        var test = new ProxyRewriterTest("NonActivityTestClass3");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        Assert.IsType<NoAttributeTypeContext>(typeMethods.Context);
        Assert.Equal("NonActivityTestClass3", typeMethods.TypeName);
        Assert.Equal("OpenTelemetry.Proxy.StandardFiles.NonActivityTestClass3", typeMethods.TypeFullName);

        Assert.IsType<SuppressInstrumentationContext>(Assert.Single(typeMethods.MethodContexts.Values));
    }
}
