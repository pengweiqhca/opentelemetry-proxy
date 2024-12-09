namespace OpenTelemetry.StaticProxy.Tests.StandardTest;

public class ActivityTest
{
    [Fact]
    public async Task NotIncludeNonAsyncStateMachineMethod()
    {
        var test = new ProxyVisitorTest("ActivityTestClass1");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        var activitySourceContext = Assert.IsAssignableFrom<ActivitySourceContext>(typeMethods.Context);

        Assert.False(activitySourceContext.IncludeNonAsyncStateMachineMethod);
        Assert.False(activitySourceContext.SuppressInstrumentation);
        Assert.Equal("TestName", activitySourceContext.VariableName);
        Assert.Equal("ActivityTestClass", activitySourceContext.ActivitySourceName);
#if NETFRAMEWORK
        Assert.Equal("(System.Diagnostics.ActivityKind)" + (int)ActivityKind.Client, activitySourceContext.Kind);
#else
        Assert.Equal("ActivityKind.Client", activitySourceContext.Kind);
#endif
        var methods = typeMethods.MethodContexts.Values.ToArray();

        Assert.Equal(3, methods.Length);

        var activityContext = Assert.IsAssignableFrom<ActivityContext>(methods[0]);

        Assert.True(activityContext.SuppressInstrumentation);
        Assert.Equal("TestMethod", activityContext.ActivityName);
#if NETFRAMEWORK
        Assert.Equal("(System.Diagnostics.ActivityKind)" + (int)ActivityKind.Server, activityContext.Kind);
#else
        Assert.Equal("ActivityKind.Server", activityContext.Kind);
#endif
        activityContext = Assert.IsAssignableFrom<ActivityContext>(methods[1]);

        Assert.False(activityContext.SuppressInstrumentation);
        Assert.Equal("ActivityTestClass1.TestMethod3", activityContext.ActivityName);
#if NETFRAMEWORK
        Assert.Equal("(System.Diagnostics.ActivityKind)" + (int)ActivityKind.Client, activityContext.Kind);
#else
        Assert.Equal("ActivityKind.Client", activityContext.Kind);
#endif
        activityContext = Assert.IsAssignableFrom<ActivityContext>(methods[2]);

        Assert.False(activityContext.SuppressInstrumentation);
        Assert.Equal("ActivityTestClass1.TestMethod4", activityContext.ActivityName);
#if NETFRAMEWORK
        Assert.Equal("(System.Diagnostics.ActivityKind)" + (int)ActivityKind.Client, activityContext.Kind);
#else
        Assert.Equal("ActivityKind.Client", activityContext.Kind);
#endif
    }

    [Fact]
    public async Task IncludeNonAsyncStateMachineMethod()
    {
        var test = new ProxyVisitorTest("ActivityTestClass2");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        var activitySourceContext = Assert.IsAssignableFrom<ActivitySourceContext>(typeMethods.Context);

        Assert.True(activitySourceContext.IncludeNonAsyncStateMachineMethod);
        Assert.True(activitySourceContext.SuppressInstrumentation);
        Assert.Equal("OpenTelemetry.Proxy.StandardFiles.ActivityTestClass2`1",
            activitySourceContext.ActivitySourceName);
        Assert.Equal("default", activitySourceContext.Kind);

        var activityContext = Assert.IsAssignableFrom<ActivityContext>(Assert.Single(typeMethods.MethodContexts.Values));

        Assert.True(activityContext.SuppressInstrumentation);
        Assert.Equal("ActivityTestClass2`1.TestMethod1", activityContext.ActivityName);
        Assert.Equal("default", activityContext.Kind);
        Assert.Equal("activity@", activityContext.VariableName);
    }

    [Fact]
    public async Task TypeNoAttribute()
    {
        var test = new ProxyVisitorTest("ActivityTestClass3");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var typeMethods = Assert.Single(results);

        Assert.Equal("OpenTelemetry.Proxy.StandardFiles.ActivityTestClass3",
            Assert.IsType<TypeActivityName2Context>(typeMethods.Context).ActivitySourceName);

        var activityContext = Assert.IsAssignableFrom<ActivityContext>(Assert.Single(typeMethods.MethodContexts.Values));

        Assert.Equal("ActivityTestClass3.TestMethod1", activityContext.ActivityName);
        Assert.Equal("test", activityContext.VariableName);
    }
}
