using Castle.DynamicProxy;
using Moq;
using OpenTelemetry.Proxy;
using OpenTelemetry.Trace;

namespace OpenTelemetry.DynamicProxy.Tests;

public class ActivityNameTest
{
    private readonly TestClass2 _target = new ProxyGenerator()
        .CreateClassProxy<TestClass2>(new ActivityInterceptor(new ActivityInvokerFactory()));

    [Fact]
    public async Task Test()
    {
        var list = new List<Activity>();

        var activityProcessor = new Mock<BaseProcessor<Activity>>();

        activityProcessor.Setup(x => x.OnEnd(It.IsAny<Activity>()))
            .Callback<Activity>(list.Add);

        using var shutdownSignal = Sdk.CreateTracerProviderBuilder()
            .AddActivityNameProcessor()
            .AddProcessor(activityProcessor.Object)
            .SetSampler(new AlwaysOnSampler())
            .AddHttpClientInstrumentation()
            .Build();

        await _target.Method1().ConfigureAwait(false);

        Assert.Equal(2, list.Count);

        Assert.Equal("TestName2." + nameof(_target.Method1), list[0].DisplayName);
        Assert.Equal("GET", list[1].DisplayName);
    }
}
