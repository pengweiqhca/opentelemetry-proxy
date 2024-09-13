using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivitySource]
[ActivityName("TestClass")]
public class ActivityNameTestClass4
{
    public void TestMethod1() { }

    [ActivityName]
    public void TestMethod2() { }

    [ActivityName("Test", AdjustStartTime = true)]
    public void TestMethod3() { }
}
