using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivityName("TestClass")]
public class ActivityNameTestClass2
{
    public void TestMethod1() { }

    [ActivityName("", AdjustStartTime = true)]
    public void TestMethod2() { }

    [ActivityName("Test")]
    public void TestMethod3() { }
}
