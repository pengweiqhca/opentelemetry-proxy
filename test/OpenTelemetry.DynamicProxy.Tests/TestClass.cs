using OpenTelemetry.Proxy;

namespace OpenTelemetry.DynamicProxy.Tests;

[ActivitySource("TestActivitySource1")]
public class TestClass1
{
    public virtual void Method1() { }

    public virtual Task MethodAsync1() => Task.CompletedTask;

    public virtual async Task MethodAsyncWithStateMachine1() => await Task.CompletedTask.ConfigureAwait(false);

    [Activity("TestMethod2", Kind = ActivityKind.Client)]
    public virtual void Method2() { }

    [NonActivity(true)]
    public void Method3() { }
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
