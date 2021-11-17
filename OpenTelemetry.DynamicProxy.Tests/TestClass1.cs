namespace OpenTelemetry.DynamicProxy.Tests;

[ActivitySource("TestActivitySource1")]
public class TestClass1
{
    public virtual void Method1() { }

    [Activity("TestMethod2", Kind = ActivityKind.Client)]
    public virtual void Method2() { }

    [NonActivity]
    public void Method3() { }
}
