using OpenTelemetry.Proxy;

namespace OpenTelemetry.StaticProxy.SourceTransformer;



[ActivitySource]
public class EmptyClass1;



public class EmptyClass2;


class NoModifierClass
{
    [Activity]
    public void TestMethod()
    {




        // Some comment.
        Console.WriteLine(DateTime.Now);
    }
}
