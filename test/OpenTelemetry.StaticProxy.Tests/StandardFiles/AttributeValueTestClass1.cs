using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivityName("Test", MaxUsableTimes = MaxUsableTimes)]
// ReSharper disable once RedundantExplicitParamsArrayCreation
[ActivityTags(["abc", "d" + "e"])]
public class AttributeValueTestClass1
{
    public const int MaxUsableTimes = 3;
}
