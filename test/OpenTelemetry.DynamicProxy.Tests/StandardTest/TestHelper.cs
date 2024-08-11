using Castle.DynamicProxy;
using OpenTelemetry.Proxy;
using System.Linq.Expressions;
using System.Reflection;

namespace OpenTelemetry.DynamicProxy.Tests.StandardTest;

internal static class TestHelper
{
    public static IProxyMethod? GetProxyMethod<T>(Expression<Func<T, Delegate>> expression) =>
        ActivityInvokerHelper.GetProxyMethod(GetMethod(expression), typeof(T));

    public static Dictionary<string, (TagPosition Direction, string Expression)>
        GetActivityTags<T>(Expression<Func<T, Delegate>> expression, out string? returnValueTagName)
    {
        var method = GetMethod(expression);

        var invocation = Expression.Parameter(typeof(IInvocation), "invocation");

        var activityTags = ActivityInvokerFactory.GetActivityTags(typeof(T), method,
            ActivityInvokerFactory.GetTags(method.GetCustomAttribute<ActivityTagsAttribute>(),
                typeof(T).GetCustomAttribute<ActivityTagsAttribute>()),
            invocation, out returnValueTagName);

        var dic = new Dictionary<string, (TagPosition Direction, string Expression)>();

        foreach (var activityTagValue in activityTags)
            dic[activityTagValue.Key] = (activityTagValue.Value.Direction, activityTagValue.Value.Value.ToString());

        return dic;
    }

    private static MethodInfo GetMethod<T>(Expression<Func<T, Delegate>> expression) =>
        Assert.IsAssignableFrom<MethodInfo>(Assert.IsAssignableFrom<ConstantExpression>(
            Assert.IsAssignableFrom<MethodCallExpression>(
                Assert.IsAssignableFrom<UnaryExpression>(expression.Body).Operand).Object).Value);
}
