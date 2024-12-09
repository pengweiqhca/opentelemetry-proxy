using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace OpenTelemetry.StaticProxy.Tests.StandardTest;

public class AttributeValueTest
{
    [Fact]
    public async Task Test1()
    {
        var test = new ProxyVisitorTest("AttributeValueTestClass1");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var context = Assert.IsType<TypeActivityNameContext>(Assert.Single(results).Context);

        Assert.Equal("Test", context.ActivityName);
        Assert.True(context.AdjustStartTime);
        Assert.Equal(["abc", "de"], context.Tags.Select(x => x.Value));
    }

    [Fact]
    public async Task Test2()
    {
        var test = new ProxyVisitorTest("AttributeValueTestClass2");

        var results = await test.VisitAsync().ConfigureAwait(false);

        var context = Assert.IsType<ActivitySourceContext>(Assert.Single(results).Context);

        Assert.Equal("Test", context.ActivitySourceName);
        Assert.Equal("default", context.Kind);
        Assert.Equal(["abc", "TestName"], context.Tags.Select(x => x.Value));
    }

    [Fact]
    public async Task Test3()
    {
        var test = new ProxyVisitorTest("AttributeValueTestClass3");
#if !NETFRAMEWORK
        test.ExpectedDiagnostics.Add(new DiagnosticResult(new(
            "OTSP001",
            "Unrecognized attribute argument",
            "Unrecognized attribute argument expression '{0}'",
            "OpenTelemetry.StaticProxy",
            DiagnosticSeverity.Error,
            true)).WithSpan(6, 30, 6, 34).WithArguments("Kind"));
#endif
        var results = await test.VisitAsync().ConfigureAwait(false);

        var context = Assert.IsType<ActivitySourceContext>(Assert.Single(results).Context);

        Assert.Equal("Test", context.ActivitySourceName);
#if NETFRAMEWORK
        Assert.Equal("(System.Diagnostics.ActivityKind)" + (int)ActivityKind.Client, context.Kind);
#else
        Assert.Equal("default", context.Kind);
#endif
        Assert.Equal(["abc", "TestName"], context.Tags.Select(x => x.Value));
    }
}
