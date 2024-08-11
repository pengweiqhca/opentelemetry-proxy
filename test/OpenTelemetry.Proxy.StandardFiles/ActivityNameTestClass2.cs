using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivityName("TestClass")]
public class ActivityNameTestClass2
{
    public void TestMethod1() { }

    [ActivityName("", MaxUsableTimes = 2)]
    public void TestMethod2() { }

    [ActivityName("Test", MaxUsableTimes = 4)]
    public void TestMethod3() { }
}
