using DynamicExpresso;
using System.Linq.Expressions;
using System.Reflection;

namespace OpenTelemetry.DynamicProxy.ExpressionParser;

public class DynamicExpressionParser : IExpressionParser
{
    private static readonly MethodInfo ParseMethod =
        new Func<string, Func<string, object>>(Parse<string>).Method.GetGenericMethodDefinition();

    public Expression Parse(Expression @this, string expression) =>
        Expression.Invoke(Expression.Constant((Delegate)ParseMethod.MakeGenericMethod(@this.Type).Invoke(null, [expression])!), @this);

    private static Func<T, object> Parse<T>(string expression) =>
        new Interpreter().ParseAsDelegate<Func<T, object>>("this" + expression[1..], "this");
}
