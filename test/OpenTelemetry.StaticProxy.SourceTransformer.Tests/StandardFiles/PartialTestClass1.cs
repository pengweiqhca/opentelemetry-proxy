using OpenTelemetry.Proxy;
using System;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivitySource(nameof(Now))]
public partial class PartialTestClass1
{
    public partial void TestMethod();
}

public partial class PartialTestClass1
{
    public static DateTime Now => DateTime.Now;

    [Activity]
    public partial void TestMethod()
    {
        Console.WriteLine(DateTime.Now);
    }
}
