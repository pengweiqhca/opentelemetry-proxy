using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivityName(AdjustStartTime = true)]
public class ActivityNameTestClass1
{
    public void TestMethod1() { }

    [ActivityName(" ", AdjustStartTime = false)]
    public void TestMethod2() { }

    [ActivityName("Test", AdjustStartTime = false)]
    public void TestMethod3() { }

    private void TestMethod4() { }
}
