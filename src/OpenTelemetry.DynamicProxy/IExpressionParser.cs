using System.Linq.Expressions;

namespace OpenTelemetry.DynamicProxy;

public interface IExpressionParser
{
    Expression Parse(Expression @this, string expression);
}

internal sealed class DefaultExpressionParser : IExpressionParser
{
    private static readonly char[] Separators = ['.'];

    public Expression Parse(Expression @this, string expression)
    {
        var members = expression.Split(Separators);

        switch (members.FirstOrDefault())
        {
            case "$?":
                return ExpressionHelper.Condition(@this, GetNestExpression(@this, expression, members.AsSpan(1)));
            case "$":
                return GetNestExpression(@this, expression, members.AsSpan(1));
            default:
                ExpressionHelper.ThrowArgumentException(expression);
                return @this;
        }
    }

    private static Expression GetNestExpression(Expression current, string expression, ReadOnlySpan<string> members)
    {
        for (var index = 0; index < members.Length; index++)
        {
            var member = members[index].AsSpan();
            if (member.Length < 1) ExpressionHelper.ThrowArgumentException(expression);

            var nullable = false;
            if (member[^1] == '?')
            {
                if (index == members.Length - 1) ExpressionHelper.ThrowArgumentException(expression);

                nullable = true;
                member = member[..^1];
            }

            if (member[^1] == ')')
            {
                if (member.Length < 3 || member[^2] != '(') ExpressionHelper.NotSupportMethodArgs(expression);

                current = Expression.Call(current, member[..^2].ToString(), Type.EmptyTypes);

                if (current.Type == typeof(void)) ExpressionHelper.ThrowArgumentException(expression);
            }
            else current = Expression.PropertyOrField(current, member.ToString());

            if (!nullable) continue;

            if (current.Type.IsValueType &&
                (!current.Type.IsGenericType || current.Type.GetGenericTypeDefinition() != typeof(Nullable<>)))
                ExpressionHelper.ThrowArgumentException(expression);

            var variable = Expression.Variable(current.Type);

            return Expression.Block(Expression.Assign(variable, current),
                ExpressionHelper.Condition(variable, GetNestExpression(variable, expression, members[(index + 1)..])));
        }

        return ExpressionHelper.ConvertToObject(current);
    }
}
