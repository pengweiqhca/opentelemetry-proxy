using Mono.Cecil;
using OpenTelemetry.Proxy;
using System.Linq.Expressions;
using System.Reflection;

namespace OpenTelemetry.StaticProxy.Fody.Tests.StandardTest;

internal static class TestHelper
{
    private static readonly EmitContext EmitContext = new(
        AssemblyDefinition.ReadAssembly(typeof(StaticProxyEmitterTest).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(Activity).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(SuppressInstrumentationScope).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(BaseProvider).Assembly.Location).MainModule,
        AssemblyDefinition.ReadAssembly(typeof(ActivityAttribute).Assembly.Location).MainModule);

    public static ProxyType<MethodDefinition> GetProxyType<T>() =>
        ActivityInvokerHelper.GetProxyType(Load<T>(), EmitContext);

    public static IProxyMethod? GetProxyMethod<T>(Expression<Func<T, Delegate>> expression)
    {
        var method = Load<T>(GetMethod(expression));

        var type = method.Module.ImportReference(typeof(T)).Resolve();

        var (tuple1, tuple2) = ActivityInvokerHelper.GetTypeAttribute(type, EmitContext);

        return ActivityInvokerHelper.GetProxyMethod(type, method, EmitContext, tuple1, tuple2);
    }

    private static MethodInfo GetMethod<T>(Expression<Func<T, Delegate>> expression) =>
        Assert.IsAssignableFrom<MethodInfo>(Assert.IsAssignableFrom<ConstantExpression>(
            Assert.IsAssignableFrom<MethodCallExpression>(
                Assert.IsAssignableFrom<UnaryExpression>(expression.Body).Operand).Object).Value);

    private static TypeDefinition Load<T>() => AssemblyDefinition
        .ReadAssembly(typeof(T).Assembly.Location).MainModule
        .ImportReference(typeof(T)).Resolve();

    private static MethodDefinition Load<T>(Expression<Action<T>> expression) =>
        Load<T>(((MethodCallExpression)expression.Body).Method);

    private static MethodDefinition Load<T>(Delegate @delegate) => Load<T>(@delegate.Method);

    private static MethodDefinition Load<T>(MethodBase method) => AssemblyDefinition
        .ReadAssembly(typeof(T).Assembly.Location).MainModule
        .ImportReference(method).Resolve();
}
