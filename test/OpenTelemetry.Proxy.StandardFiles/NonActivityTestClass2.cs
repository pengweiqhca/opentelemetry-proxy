using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivityName]
public class NonActivityTestClass2
{
    [NonActivity]
    public static bool TestMethod1() => Sdk.SuppressInstrumentation;

    [NonActivity(true)]
    public static bool TestMethod2() => Sdk.SuppressInstrumentation;
}
