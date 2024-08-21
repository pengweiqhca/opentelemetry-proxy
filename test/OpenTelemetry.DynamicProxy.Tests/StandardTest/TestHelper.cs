using Castle.DynamicProxy;
using OpenTelemetry.Proxy;
using System.Linq.Expressions;
using System.Reflection;
using Tags =
    (System.Collections.Generic.Dictionary<OpenTelemetry.ActivityTag, string> In,
    System.Collections.Generic.Dictionary<OpenTelemetry.ActivityTag, string> Out,
    System.Tuple<System.Collections.Generic.HashSet<OpenTelemetry.ActivityTag>, System.Type>? Return);

namespace OpenTelemetry.DynamicProxy.Tests.StandardTest;

internal static class TestHelper
{
    public static IProxyMethod? GetProxyMethod<T>(Expression<Func<T, Delegate>> expression) =>
        ActivityInvokerHelper.GetProxyMethod(GetMethod(expression), typeof(T));

    public static Tags GetActivityTags<T>(Expression<Func<T, Delegate>> expression)
    {
        var method = GetMethod(expression);

        var invocation = Expression.Parameter(typeof(IInvocation), "invocation");

        var (inTags, outTags, ret) = ActivityInvokerFactory.GetActivityTags(typeof(T), method,
            ActivityInvokerFactory.GetTags(method, typeof(T)), invocation);

        return (inTags.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()),
            outTags.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()), ret);
    }

    private static MethodInfo GetMethod<T>(Expression<Func<T, Delegate>> expression) =>
        Assert.IsAssignableFrom<MethodInfo>(Assert.IsAssignableFrom<ConstantExpression>(
            Assert.IsAssignableFrom<MethodCallExpression>(
                Assert.IsAssignableFrom<UnaryExpression>(expression.Body).Operand).Object).Value);
}
