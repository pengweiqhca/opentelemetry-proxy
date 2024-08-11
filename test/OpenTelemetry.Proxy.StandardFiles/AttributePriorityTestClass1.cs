using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivityName]
[ActivitySource]
public class AttributePriorityTestClass1
{
    [Activity]
    [ActivityName]
    [NonActivity]
    public void TestMethod1() { }

    [Activity]
    [ActivityName]
    public void TestMethod2() { }
}
