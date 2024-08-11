using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivitySource(IncludeNonAsyncStateMachineMethod = true, SuppressInstrumentation = true)]
public class ActivityTestClass2<T>
{
    public void TestMethod1() { }

    private void TestMethod2() { }
}
