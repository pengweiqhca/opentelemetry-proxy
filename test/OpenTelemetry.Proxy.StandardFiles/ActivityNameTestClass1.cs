using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivityName(MaxUsableTimes = 3)]
public class ActivityNameTestClass1
{
    public void TestMethod1() { }

    [ActivityName(" ", MaxUsableTimes = 2)]
    public void TestMethod2() { }

    [ActivityName("Test", MaxUsableTimes = 4)]
    public void TestMethod3() { }

    private void TestMethod4() { }
}
