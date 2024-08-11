using OpenTelemetry.Proxy;

namespace OpenTelemetry.StaticProxy.SourceTransformer;




/// <summary>
/// KeepLineNumberTestClass
/// </summary>
[ActivitySource]
public class KeepLineNumberTestClass
{
    [Activity]
    [return: ActivityTag]
    public static int TestMethod() =>



        // Some comment.
        throw new();

    public static class NormalClass
    {
        public static Exception Exception() => throw new();
    }
}
