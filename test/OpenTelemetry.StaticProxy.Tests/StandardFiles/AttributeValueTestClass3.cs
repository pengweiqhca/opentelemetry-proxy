using OpenTelemetry.Proxy;
using System.Diagnostics;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivitySource(Name, Kind = Kind)]
[ActivityTags("abc", Name + nameof(Name))]
public class AttributeValueTestClass3
{
    public const string Name = "Test";
    public const ActivityKind Kind = ActivityKind.Client;
}
