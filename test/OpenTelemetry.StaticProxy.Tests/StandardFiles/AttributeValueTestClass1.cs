using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

[ActivityName("Test", AdjustStartTime = AdjustStartTime)]
// ReSharper disable once RedundantExplicitParamsArrayCreation
[ActivityTags(["abc", "d" + "e"])]
public class AttributeValueTestClass1
{
    public const bool AdjustStartTime = true;
}
