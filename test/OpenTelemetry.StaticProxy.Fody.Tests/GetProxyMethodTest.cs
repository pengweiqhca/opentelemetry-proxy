using Mono.Cecil;
using OpenTelemetry.Proxy;
using System.Linq.Expressions;
using System.Reflection;

namespace OpenTelemetry.StaticProxy.Fody.Tests;

public class GetProxyMethodTest
{
    private static readonly EmitContext EmitContext = GetEmitContext();

    [Fact]
    public void NoActivityTest()
    {
        var (settings, _, _, _) = ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(x => x.Test0()), EmitContext,
            new(2, true, false), new(Guid.NewGuid().ToString("N"), 3));

        Assert.Equal(ActivitySettings.None, settings);

        (settings, _, _, _) = ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(x => x.Test2()), EmitContext,
            new(2, true, false), new(Guid.NewGuid().ToString("N"), 3));

        Assert.Equal(ActivitySettings.SuppressInstrumentation, settings);
    }

    [Fact]
    public void ActivityTest()
    {
        var (settings, activityName, kind, _) = ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(x => x.Test3()),
            EmitContext, new(2, true, false), new(Guid.NewGuid().ToString("N"), 3));

        Assert.Equal(ActivitySettings.Activity, settings);
        Assert.Equal("test", activityName);
        Assert.Equal((int)ActivityKind.Client, kind);

        (settings, activityName, kind, _) = ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(x => x.Test5()),
            EmitContext, new(2, true, false), new(Guid.NewGuid().ToString("N"), 3));

        Assert.Equal(ActivitySettings.ActivityAndSuppressInstrumentation, settings);
        Assert.Null(activityName);
        Assert.Equal(default, kind);
    }

    [Fact]
    public void ActivityBaseTest()
    {
        var (settings, _, _, _) = ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(x => x.TestBase()),
            EmitContext, new(2, true, false), new(Guid.NewGuid().ToString("N"), 3));

        Assert.Equal(ActivitySettings.Activity, settings);
    }

    [Fact]
    public void ActivityNameTest()
    {
        var (settings, activityName, _, maxUsableTimes) =
            ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(x => x.Test4()), EmitContext, null, null);

        Assert.Equal(ActivitySettings.ActivityName, settings);
        Assert.Equal("test", activityName);
        Assert.Equal(5, maxUsableTimes);
    }

    [Fact]
    public void DefaultIgnorePrivateMethod()
    {
        var (settings, _, _, _) = ActivityInvokerHelper.GetProxyMethod(
            Load<TestClass>(typeof(TestClass).GetMethod("PrivateMethod",
                BindingFlags.Instance | BindingFlags.NonPublic)!), EmitContext, null, null);

        Assert.Equal(ActivitySettings.None, settings);
    }

    [Fact]
    public void DefaultActivitySourceTest()
    {
        var (settings, _, _, _) =
            ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(TestClass.DefaultSync), EmitContext, new(0, true, false), null);

        Assert.Equal(ActivitySettings.Activity, settings);

        (settings, _, _, _) =
            ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(TestClass.DefaultSync), EmitContext, new(0, false, false),
                null);

        Assert.Equal(ActivitySettings.None, settings);

        (settings, _, _, _) =
            ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(TestClass.DefaultAsync), EmitContext, new(0, true, false), null);

        Assert.Equal(ActivitySettings.Activity, settings);

        (settings, _, _, _) =
            ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(TestClass.DefaultAsync), EmitContext, new(0, false, false), null);

        Assert.Equal(ActivitySettings.None, settings);

        (settings, _, _, _) =
            ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(TestClass.DefaultAsyncWithStateMachine), EmitContext, new(0, true, false), null);

        Assert.Equal(ActivitySettings.Activity, settings);

        (settings, _, _, _) =
            ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(TestClass.DefaultAsyncWithStateMachine), EmitContext, new(0, false, false), null);

        Assert.Equal(ActivitySettings.Activity, settings);

        (settings, _, _, _) =
            ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(TestClass.DefaultAsyncWithStateMachine), EmitContext, new(0, false, true), null);

        Assert.Equal(ActivitySettings.ActivityAndSuppressInstrumentation, settings);
    }

    private static MethodDefinition Load<T>(Expression<Action<T>> expression) =>
        Load<T>(((MethodCallExpression)expression.Body).Method);

    private static MethodDefinition Load<T>(Delegate @delegate) => Load<T>(@delegate.Method);

    private static MethodDefinition Load<T>(MethodBase method) => AssemblyDefinition
        .ReadAssembly(typeof(T).Assembly.Location).MainModule
        .ImportReference(method).Resolve();

    private static EmitContext GetEmitContext() => new(
        AssemblyDefinition.ReadAssembly(typeof(StaticProxyEmitterTest).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(Activity).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(SuppressInstrumentationScope).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(BaseProvider).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(ActivityAttribute).Assembly.Location).MainModule);

    private class TestClassBase
    {
        [Activity]
        public virtual void TestBase() { }
    }

    private class TestClass : TestClassBase
    {
        [NonActivity]
        public void Test0() { }

        [NonActivity(true)]
        public void Test2() { }

        [Activity("test", Kind = ActivityKind.Client)]
        public void Test3() { }

        [ActivityName("test", MaxUsableTimes = 5)]
        public void Test4() { }

        [Activity(SuppressInstrumentation = true)]
        public void Test5() { }

        public override void TestBase() { }

        private void PrivateMethod() { }

        public static void DefaultSync() { }

        public static Task DefaultAsync() => Task.CompletedTask;

        public static async Task DefaultAsyncWithStateMachine() => await Task.CompletedTask.ConfigureAwait(false);
    }
}
