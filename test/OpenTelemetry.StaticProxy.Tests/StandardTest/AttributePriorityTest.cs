namespace OpenTelemetry.StaticProxy.Tests.StandardTest;

public class AttributePriorityTest
{
    [Fact]
    public async Task ActivitySourceTest()
    {
        var test = new ProxyRewriterTest("AttributePriorityTestClass1");

        var results = await test.VisitAsync().ConfigureAwait(false);

        Assert.IsType<ActivitySourceContext>(Assert.Single(results).Context);
        Assert.IsType<ActivityContext>(Assert.Single(results[0].MethodContexts.Values));
    }

    [Fact]
    public async Task ActivityNameTest()
    {
        var test = new ProxyRewriterTest("AttributePriorityTestClass2");

        var results = await test.VisitAsync().ConfigureAwait(false);

        Assert.IsType<TypeActivityName2Context>(Assert.Single(results).Context);
        Assert.IsType<ActivityContext>(Assert.Single(results[0].MethodContexts.Values));
    }
}
