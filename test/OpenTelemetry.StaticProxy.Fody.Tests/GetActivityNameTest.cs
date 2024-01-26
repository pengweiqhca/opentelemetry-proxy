using Mono.Cecil;
using OpenTelemetry.Proxy;
using System.Linq.Expressions;
using System.Reflection;

namespace OpenTelemetry.StaticProxy.Fody.Tests;

public class GetActivityNameTest
{
    private static readonly EmitContext EmitContext = GetEmitContext();

    [Fact]
    public void NoActivityTest()
    {
        var (settings, _, _, _) = ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(x => x.Test0()), EmitContext, 2,
            true,
            Guid.NewGuid().ToString("N"), 3);

        Assert.Equal(ActivitySettings.NonActivity, settings);

        (settings, _, _, _) = ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(x => x.Test2()), EmitContext, 2, true,
            Guid.NewGuid().ToString("N"), 3);

        Assert.Equal(ActivitySettings.NonActivityAndSuppressInstrumentation, settings);
    }

    [Fact]
    public void ActivityTest()
    {
        var (settings, activityName, kind, _) = ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(x => x.Test3()),
            EmitContext, 2, true, Guid.NewGuid().ToString("N"), 3);

        Assert.Equal(ActivitySettings.Activity, settings);
        Assert.Equal("test", activityName);
        Assert.Equal((int)ActivityKind.Client, kind);
    }

    [Fact]
    public void ActivityBaseTest()
    {
        var (settings, _, _, _) = ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(x => x.TestBase()),
            EmitContext, 2, true, Guid.NewGuid().ToString("N"), 3);

        Assert.Equal(ActivitySettings.Activity, settings);
    }

    [Fact]
    public void ActivityNameTest()
    {
        var (settings, activityName, _, maxUsableTimes) =
            ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(x => x.Test4()), EmitContext, -1, true, null, 0);

        Assert.Equal(ActivitySettings.ActivityNameOnly, settings);
        Assert.Equal("test", activityName);
        Assert.Equal(5, maxUsableTimes);
    }

    [Fact]
    public void DefaultIgnorePrivateMethod()
    {
        var (settings, _, _, _) = ActivityInvokerHelper.GetProxyMethod(
            Load<TestClass>(typeof(TestClass).GetMethod("PrivateMethod",
                BindingFlags.Instance | BindingFlags.NonPublic)!), EmitContext, -1, true, null, null);

        Assert.Equal(ActivitySettings.NonActivity, settings);
    }

    [Fact]
    public void DefaultActivitySourceTest()
    {
        var (settings, _, _, _) =
            ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(new Action(TestClass.DefaultSync).Method), EmitContext, 0, true, null, null);

        Assert.Equal(ActivitySettings.Activity, settings);

        (settings, _, _, _) =
            ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(new Action(TestClass.DefaultSync).Method), EmitContext, 0, false, null, null);

        Assert.Equal(ActivitySettings.NonActivity, settings);

        (settings, _, _, _) =
            ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(new Func<Task>(TestClass.DefaultAsync).Method), EmitContext, 0, true, null, null);

        Assert.Equal(ActivitySettings.Activity, settings);

        (settings, _, _, _) =
            ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(new Func<Task>(TestClass.DefaultAsync).Method), EmitContext, 0, false, null, null);

        Assert.Equal(ActivitySettings.NonActivity, settings);

        (settings, _, _, _) =
            ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(new Func<Task>(TestClass.DefaultAsyncWithStateMachine).Method), EmitContext, 0, true, null, null);

        Assert.Equal(ActivitySettings.Activity, settings);

        (settings, _, _, _) =
            ActivityInvokerHelper.GetProxyMethod(Load<TestClass>(new Func<Task>(TestClass.DefaultAsyncWithStateMachine).Method), EmitContext, 0, false, null, null);

        Assert.Equal(ActivitySettings.Activity, settings);
    }

    private static MethodDefinition Load<T>(Expression<Action<T>> expression) =>
        Load<T>(((MethodCallExpression)expression.Body).Method);

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

        public override void TestBase() { }

        private void PrivateMethod() { }

        public static void DefaultSync() { }

        public static Task DefaultAsync() => Task.CompletedTask;

        public static async Task DefaultAsyncWithStateMachine() => await Task.CompletedTask.ConfigureAwait(false);
    }
}
