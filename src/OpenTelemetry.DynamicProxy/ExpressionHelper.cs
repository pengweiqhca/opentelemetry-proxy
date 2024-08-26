using OpenTelemetry.Proxy;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace OpenTelemetry.DynamicProxy;

internal static class ExpressionHelper
{
    private static readonly MethodInfo SetTagEnumerable =
        typeof(ActivityExtensions).GetMethod(nameof(ActivityExtensions.SetTagEnumerable))!;

    public static MethodCallExpression SetTag(IExpressionParser parser, Expression activity, string tag, string? expression, Expression from) =>
        SetTag(activity, tag, ParseExpression(parser, expression, from));

    public static MethodCallExpression SetTag(Expression activity, string tag, Expression from) =>
        Expression.Call(SetTagEnumerable, activity, Expression.Constant(tag), from);

    public static Expression ConvertToObject(Expression expression) =>
        expression.Type.IsValueType || expression.Type.IsGenericParameter
            ? Expression.Convert(expression, typeof(object))
            : expression;

    public static Expression ParseExpression(IExpressionParser parser, string? expression, Expression from) =>
        ConvertToObject(IsExpression(expression) ? parser.Parse(from, expression) : from);

    public static bool IsExpression([NotNullWhen(true)] string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;

        if (expression![0] != '$') ThrowArgumentException(expression);

        return true;
    }

    public static ConditionalExpression Condition(Expression variable, Expression nestExpression) =>
        Expression.Condition(Expression.Equal(variable, Expression.Constant(null, variable.Type)),
            Expression.Constant(null, nestExpression.Type), nestExpression);

    [DoesNotReturn]
    public static void ThrowArgumentException(string expression) =>
        throw new ArgumentException($"`{expression}` is not a valid expression.");

    [DoesNotReturn]
    public static void NotSupportMethodArgs(string expression) =>
        throw new NotSupportedException($"`{expression}` is not a supported expression, because not support method with argument(s).");
}
