using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivitySource(Name, Kind = default)]
// ReSharper disable once RedundantExplicitParamsArrayCreation
// ReSharper disable once UseCollectionExpression
[ActivityTags(new[] { "abc", Name + nameof(Name) })]
public class AttributeValueTestClass2
{
    public const string Name = "Test";
}
