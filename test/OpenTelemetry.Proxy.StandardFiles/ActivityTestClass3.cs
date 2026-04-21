using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivityName]
public class ActivityTestClass3
{
    [Activity(" ")]
    public void TestMethod1() { }
}
