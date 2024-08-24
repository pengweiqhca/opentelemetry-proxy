using OpenTelemetry.Proxy;

namespace OpenTelemetry.StaticProxy;




/// <summary>
/// KeepLineNumberTestClass
/// </summary>
[ActivitySource]
public class KeepLineNumberTestClass<T>
{
    [Activity]
    [return: ActivityTag]
    public static int TestMethod(int size)
    {
        if (size < 10) return 1;

        Console.WriteLine(DateTime.Now);

        throw new();
    }

    public static class NormalClass
    {
        public static Exception TestMethod() => throw new();
    }
}
