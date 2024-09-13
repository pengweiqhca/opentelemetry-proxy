using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

public class ActivityNameTestClass3
{
    public void TestMethod1() { }

    [ActivityName(AdjustStartTime = true)]
    public void TestMethod2() { }

    [ActivityName("Test")]
    public void TestMethod3() { }
}
