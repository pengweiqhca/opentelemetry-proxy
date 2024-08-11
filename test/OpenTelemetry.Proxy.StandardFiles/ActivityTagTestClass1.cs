using OpenTelemetry.Proxy;

namespace OpenTelemetry.Proxy.StandardFiles;

public class ActivityTagTestClass1
{
    [ActivityName]
    // ReSharper disable once RedundantExplicitParamsArrayCreation
    [ActivityTags(["test", nameof(abc)])]
    [return: ActivityTag]
    public int TestMethod1(int abc, [ActivityTag("def")] int age) => age;
}
