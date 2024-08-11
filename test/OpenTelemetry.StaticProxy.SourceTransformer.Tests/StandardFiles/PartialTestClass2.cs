using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

public partial class PartialTestClass2
{
    public const string Name = "Test";

    [Activity]
    public partial void TestMethod();
}

[ActivitySource]
[ActivityTags(Name + nameof(Name))]
public partial class PartialTestClass2
{
    public partial void TestMethod() { }
}
