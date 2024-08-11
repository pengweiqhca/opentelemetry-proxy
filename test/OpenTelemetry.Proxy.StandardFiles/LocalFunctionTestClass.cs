using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

public class LocalFunctionTestClass
{
    public void Test()
    {
        InlineMethod();

        [Activity]
        static void InlineMethod()
        {
            TestMethod();

            static void TestMethod() { }
        }
    }
}
