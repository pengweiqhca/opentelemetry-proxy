using OpenTelemetry.Proxy;

namespace OpenTelemetry.StaticProxy.SourceTransformer;



[ActivitySource]
public class EmptyClass1;



public class EmptyClass2;


class NoModifierClass
{
    [Activity]
    [return: ActivityTag]
    public int TestMethod([ActivityTag] ref int abc)
    {
        if (DateTime.Now.Second > 10) return 1;

        return abc;
    }
}
