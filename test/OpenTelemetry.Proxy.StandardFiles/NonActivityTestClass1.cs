using OpenTelemetry.Proxy;
using System.Diagnostics;

namespace OpenTelemetry.Proxy.StandardFiles;

public class NonActivityTestClass1
{
    [ActivitySource(Kind = ActivityKind.Client)]
    public class TestClass1
    {
        [NonActivity]
        public bool TestMethod1() => Sdk.SuppressInstrumentation;

        [NonActivity(true)]
        public bool TestMethod2() => Sdk.SuppressInstrumentation;
    }
}
