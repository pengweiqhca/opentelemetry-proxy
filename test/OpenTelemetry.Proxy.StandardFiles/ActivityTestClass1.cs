using OpenTelemetry.Proxy;
using System.Diagnostics;
using System.Threading.Tasks;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivitySource("ActivityTestClass", Kind = ActivityKind.Client)]
public class ActivityTestClass1
{
    public void TestMethod1() { }

    [Activity("TestMethod", SuppressInstrumentation = true, Kind = ActivityKind.Server)]
    public void TestMethod2() { }

    public async void TestMethod3() => await Task.CompletedTask.ConfigureAwait(false);

    public async Task TestMethod4() => await Task.CompletedTask.ConfigureAwait(false);

    public Task TestMethod5() => Task.CompletedTask;

    private Task TestMethod6() => Task.CompletedTask;
}
