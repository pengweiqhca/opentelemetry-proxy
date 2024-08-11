using Castle.DynamicProxy;
using Moq;
using OpenTelemetry.Proxy;
using OpenTelemetry.Trace;
using System.Net.Http;

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

    [ActivityName("TestName2")]
    public class TestClass2
    {
        public virtual async Task Method1()
        {
            using var client = new HttpClient();

            await client.GetStringAsync("https://docs.microsoft.com/_themes/docs.theme/master/zh-cn/_themes/styles/9b70df4.site-ltr.css").ConfigureAwait(false);
            await client.GetStringAsync("https://docs.microsoft.com/_themes/docs.theme/master/zh-cn/_themes/styles/9b70df4.site-ltr.css").ConfigureAwait(false);
        }
    }
}
