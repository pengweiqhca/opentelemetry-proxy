using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivityName]
public class ActivityTestClass3
{
    [Activity(" ", VariableName = "test")]
    public void TestMethod1() { }
}
