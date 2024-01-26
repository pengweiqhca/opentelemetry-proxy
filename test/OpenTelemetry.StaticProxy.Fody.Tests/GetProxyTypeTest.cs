using Mono.Cecil;
using OpenTelemetry.Proxy;

namespace OpenTelemetry.StaticProxy.Fody.Tests;

public class GetProxyTypeTest
{
    private static readonly EmitContext EmitContext = new(
        AssemblyDefinition.ReadAssembly(typeof(StaticProxyEmitterTest).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(Activity).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(SuppressInstrumentationScope).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(BaseProvider).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(ActivityAttribute).Assembly.Location).MainModule);

    [Fact]
    public void ActivityNameInlineMethodTest()
    {
        var proxyType = ActivityInvokerHelper.GetProxyType(Load<TestClass>(), EmitContext);

        Assert.Equal(3, proxyType.Methods.Count);

        Assert.Equal(["Test", "Test2", "<Test2>g__InlineMethod|1_0"], proxyType.Methods.Keys.Select(m => m.Name));

        Assert.Equal(ActivitySettings.ActivityName, proxyType.Methods[proxyType.Methods.Keys.ElementAt(0)].Settings);
        Assert.Equal(ActivitySettings.ActivityName, proxyType.Methods[proxyType.Methods.Keys.ElementAt(1)].Settings);
        Assert.Equal(ActivitySettings.Activity, proxyType.Methods[proxyType.Methods.Keys.ElementAt(2)].Settings);
    }

    [Fact]
    public void ActivitySourceInlineMethodTest()
    {
        var proxyType = ActivityInvokerHelper.GetProxyType(Load<TestClass2>(), EmitContext);

        Assert.Equal(3, proxyType.Methods.Count);

        Assert.Equal(["Test", "Test2", "<Test2>g__InlineMethod|1_0"], proxyType.Methods.Keys.Select(m => m.Name));

        Assert.Equal(ActivitySettings.Activity, proxyType.Methods[proxyType.Methods.Keys.ElementAt(0)].Settings);
        Assert.Equal(ActivitySettings.Activity, proxyType.Methods[proxyType.Methods.Keys.ElementAt(1)].Settings);
        Assert.Equal(ActivitySettings.ActivityName, proxyType.Methods[proxyType.Methods.Keys.ElementAt(2)].Settings);
    }

    private static TypeDefinition Load<T>() => AssemblyDefinition
        .ReadAssembly(typeof(T).Assembly.Location).MainModule
        .ImportReference(typeof(T)).Resolve();

    [ActivityName]
    private class TestClass
    {
        public void Test()
        {
            InlineMethod();

            static void InlineMethod() { }
        }

        public void Test2()
        {
            InlineMethod();

            [Activity]
            static void InlineMethod() { }
        }
    }

    [ActivitySource(IncludeNonAsyncStateMachineMethod = true)]
    private class TestClass2
    {
        public void Test()
        {
            InlineMethod();

            static void InlineMethod() { }
        }

        public void Test2()
        {
            InlineMethod();

            [ActivityName]
            static void InlineMethod() { }
        }
    }
}
