using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

public class ActivityTestClass3
{
    [Activity(" ")]
    public void TestMethod1() { }
}
