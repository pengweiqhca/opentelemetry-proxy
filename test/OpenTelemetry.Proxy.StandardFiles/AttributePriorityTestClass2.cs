using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivityName]
public class AttributePriorityTestClass2
{
    [Activity]
    [ActivityName]
    [NonActivity]
    public void TestMethod1() { }

    [Activity]
    [ActivityName]
    public void TestMethod2() { }
}
