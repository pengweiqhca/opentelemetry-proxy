namespace OpenTelemetry.StaticProxy.Tests.StandardTest;

public class ActivityNameTest
{
    [Fact]
    public async Task ActivityNameNoName()
    {
        var test = new ProxyVisitorTest("ActivityNameTestClass1");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        AssertActivityNameContext(Assert.IsAssignableFrom<ActivityNameContext>(typeMethods.Context),
            "ActivityNameTestClass1", true);

        var methods = typeMethods.MethodContexts.Values.ToArray();

        Assert.Equal(3, methods.Length);

        AssertActivityNameContext(Assert.IsAssignableFrom<ActivityNameContext>(Assert.IsAssignableFrom<ActivityNameContext>(methods[0])),
            "ActivityNameTestClass1.TestMethod1", true);

        AssertActivityNameContext(Assert.IsAssignableFrom<ActivityNameContext>(Assert.IsAssignableFrom<ActivityNameContext>(methods[1])),
            "ActivityNameTestClass1.TestMethod2", false);

        AssertActivityNameContext(Assert.IsAssignableFrom<ActivityNameContext>(Assert.IsAssignableFrom<ActivityNameContext>(methods[2])),
            "Test", false);
    }

    [Fact]
    public async Task ActivityNameHaveName()
    {
        var test = new ProxyVisitorTest("ActivityNameTestClass2");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        AssertActivityNameContext(Assert.IsAssignableFrom<ActivityNameContext>(typeMethods.Context),
            "TestClass", false);

        var methods = typeMethods.MethodContexts.Values.ToArray();

        Assert.Equal(3, methods.Length);

        AssertActivityNameContext(Assert.IsAssignableFrom<ActivityNameContext>(Assert.IsAssignableFrom<ActivityNameContext>(methods[0])),
            "TestClass.TestMethod1", false);

        AssertActivityNameContext(Assert.IsAssignableFrom<ActivityNameContext>(Assert.IsAssignableFrom<ActivityNameContext>(methods[1])),
            "TestClass.TestMethod2", true);

        AssertActivityNameContext(Assert.IsAssignableFrom<ActivityNameContext>(Assert.IsAssignableFrom<ActivityNameContext>(methods[2])),
            "Test", false);
    }

    [Fact]
    public async Task TypeHaveNoActivityName()
    {
        var test = new ProxyVisitorTest("ActivityNameTestClass3");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        Assert.IsAssignableFrom<NoAttributeTypeContext>(typeMethods.Context);

        var methods = typeMethods.MethodContexts.Values.ToArray();

        Assert.Equal(2, methods.Length);

        AssertActivityNameContext(Assert.IsAssignableFrom<ActivityNameContext>(Assert.IsAssignableFrom<ActivityNameContext>(methods[0])),
            "ActivityNameTestClass3.TestMethod2", true);

        AssertActivityNameContext(Assert.IsAssignableFrom<ActivityNameContext>(Assert.IsAssignableFrom<ActivityNameContext>(methods[1])),
            "Test", false);
    }

    [Fact]
    public async Task TypeHaveActivityNameAndActivitySource()
    {
        var test = new ProxyVisitorTest("ActivityNameTestClass4");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        Assert.IsAssignableFrom<ActivitySourceContext>(typeMethods.Context);

        var methods = typeMethods.MethodContexts.Values.ToArray();

        Assert.Equal(2, methods.Length);

        AssertActivityNameContext(Assert.IsAssignableFrom<ActivityNameContext>(Assert.IsAssignableFrom<ActivityNameContext>(methods[0])),
            "ActivityNameTestClass4.TestMethod2", false);

        AssertActivityNameContext(Assert.IsAssignableFrom<ActivityNameContext>(Assert.IsAssignableFrom<ActivityNameContext>(methods[1])),
            "Test", true);
    }

    private static void AssertActivityNameContext(ActivityNameContext context, string activityName, bool adjustStartTime)
    {
        Assert.Equal(activityName, context.ActivityName);
        Assert.Equal(adjustStartTime, context.AdjustStartTime);
    }
}
