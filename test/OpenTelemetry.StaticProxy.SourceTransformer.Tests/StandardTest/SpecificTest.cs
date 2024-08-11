namespace OpenTelemetry.StaticProxy.SourceTransformer.Tests.StandardTest;

public class SpecificTest
{
#if NET6_0_OR_GREATER
    [Fact]
    public async Task InterfaceTest()
    {
        var test = new ProxyRewriterTest("TestInterface");

        var results = await test.VisitAsync().ConfigureAwait(false);

        Assert.Equal(2, Assert.Single(results).MethodContexts.Count);
    }
#endif
    /*[Fact]
    public async Task LocalFunctionTest()
    {
        var test = new ProxyRewriterTest("LocalFunctionTestClass");

        var results = await test.VisitAsync().ConfigureAwait(false);

        Assert.Equal("LocalFunctionTestClass.Test+InlineMethod", Assert.IsType<ActivityContext>(Assert.Single(Assert.Single(results).MethodContexts.Values)).ActivityName);
    }*/
}
