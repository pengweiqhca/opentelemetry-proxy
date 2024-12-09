namespace OpenTelemetry.StaticProxy.Tests.StandardTest;

public class PartialTest
{
    [Fact]
    public async Task Test1()
    {
        var test = new ProxyVisitorTest("PartialTestClass1");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        Assert.Equal("Now", Assert.IsAssignableFrom<ActivitySourceContext>(typeMethods.Context).ActivitySourceName);

        Assert.Equal("PartialTestClass1.TestMethod",
            Assert.IsAssignableFrom<ActivityContext>(Assert.Single(typeMethods.MethodContexts.Values)).ActivityName);
    }

    [Fact]
    public async Task Test2()
    {
        var test = new ProxyVisitorTest("PartialTestClass2");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        Assert.Equal(["TestName"], Assert.IsAssignableFrom<ActivitySourceContext>(typeMethods.Context).Tags.Select(x => x.Value));

        Assert.Equal("PartialTestClass2.TestMethod",
            Assert.IsAssignableFrom<ActivityContext>(Assert.Single(typeMethods.MethodContexts.Values)).ActivityName);
    }
}
