using Castle.DynamicProxy;
using OpenTelemetry.Proxy.StandardFiles;
using System.Linq.Expressions;

namespace OpenTelemetry.DynamicProxy.Tests.StandardTest;

public class ActivityTagTest
{
    [Fact]
    public void ActivityTagsTest()
    {
        var tags = TestHelper.GetActivityTags<ActivityTagTestClass1>(x => x.TestMethod1, out var returnValueTagName);

        Assert.Equal("$returnvalue", returnValueTagName);

        Assert.Equal(new()
        {
            ["abc"] = (TagPosition.Start, "invocation.GetArgumentValue(0)"),
            ["def"] = (TagPosition.Start, "invocation.GetArgumentValue(1)")
        }, tags);
    }

    [Fact]
    public void ReturnValueTest()
    {
        var tags = TestHelper.GetActivityTags<ActivityTagTestClass2>(x => x.InstanceMethod, out var returnValueTagName);

        Assert.Equal("ghi", returnValueTagName);

        Assert.Equal(new()
        {
            ["a2"] = (TagPosition.Start, "invocation.GetArgumentValue(0)"),
            ["b"] = (TagPosition.Start, "invocation.GetArgumentValue(2)"),
            ["c"] = (TagPosition.End, "invocation.GetArgumentValue(3)"),
            ["d"] = (TagPosition.All, "invocation.GetArgumentValue(4)"),
            ["e"] = (TagPosition.Start, "invocation.GetArgumentValue(5)"),
#if NETFRAMEWORK
            ["_now"] = (TagPosition.Start, "Convert(invocation.InvocationTarget)._now"),
#else
            ["_now"] = (TagPosition.Start, "Convert(invocation.InvocationTarget, ActivityTagTestClass2)._now"),
#endif
#if NETFRAMEWORK
            ["Now"] = (TagPosition.Start, "Convert(invocation.InvocationTarget).Now")
#else
            ["Now"] = (TagPosition.Start, "Convert(invocation.InvocationTarget, ActivityTagTestClass2).Now")
#endif
        }, tags);
    }

    [Fact]
    public void TypeHaveTagsTest()
    {
        var dic = new Dictionary<string, (TagPosition Direction, string Expression)>
        {
            ["abc"] = (TagPosition.Start, "invocation.GetArgumentValue(0)"),
            ["age"] = (TagPosition.Start, "invocation.GetArgumentValue(1)"),
            ["Abc"] = (TagPosition.Start, "ActivityTagTestClass3.Abc"),
#if NETFRAMEWORK
            ["_abc"] = (TagPosition.Start, "Convert(invocation.InvocationTarget)._abc")
#else
            ["_abc"] = (TagPosition.Start, "Convert(invocation.InvocationTarget, ActivityTagTestClass3)._abc")
#endif
        };

        Assert.Equal(dic, TestHelper.GetActivityTags<ActivityTagTestClass3>(x => x.InstanceMethod, out var returnValueTagName));

        Assert.Null(returnValueTagName);
    }

    [Fact]
    public void TypeNoTagsTest()
    {
        var dic = new Dictionary<string, (TagPosition Direction, string Expression)>
        {
            ["abc"] = (TagPosition.Start, "invocation.GetArgumentValue(0)"),
            ["def"] = (TagPosition.Start, "invocation.GetArgumentValue(1)"),
            ["Abc"] = (TagPosition.Start, "ActivityTagTestClass4.Abc"),
#if NETFRAMEWORK
            ["_abc"] = (TagPosition.Start, "Convert(invocation.InvocationTarget)._abc")
#else
            ["_abc"] = (TagPosition.Start, "Convert(invocation.InvocationTarget, ActivityTagTestClass4)._abc")
#endif
        };

        Assert.Equal(dic, TestHelper.GetActivityTags<ActivityTagTestClass4>(x => x.InstanceMethod, out _));
    }

    [Fact]
    public void GetActivityTags()
    {
        HashSet<string> tags = ["_now", "Now", "e", Guid.NewGuid().ToString("N")];
        var invocation = Expression.Parameter(typeof(IInvocation), "invocation");

        var activityTags = ActivityInvokerFactory.GetActivityTags(typeof(ActivityTagTestClass2),
            typeof(ActivityTagTestClass2).GetMethod(nameof(ActivityTagTestClass2.InstanceMethod))!, tags, invocation,
            out var returnValueTagName).ToArray();

        Assert.Equal("ghi", returnValueTagName);
        Assert.Equal(7, activityTags.Length);

        AssertNameAndPosition(activityTags[0], "a2", TagPosition.Start);
        AssertCallExpression(activityTags[0].Value.Value, invocation, 0);

        AssertNameAndPosition(activityTags[1], "b", TagPosition.Start);
        AssertCallExpression(activityTags[1].Value.Value, invocation, 2);

        AssertNameAndPosition(activityTags[2], "c", TagPosition.End);
        AssertCallExpression(activityTags[2].Value.Value, invocation, 3);

        AssertNameAndPosition(activityTags[3], "d", TagPosition.All);
        AssertCallExpression(activityTags[3].Value.Value, invocation, 4);

        AssertNameAndPosition(activityTags[4], "e", TagPosition.Start);
        AssertCallExpression(activityTags[4].Value.Value, invocation, 5);

        AssertNameAndPosition(activityTags[5], "_now", TagPosition.Start);
        AssertConvertExpression(AssertMemberExpression(activityTags[5].Value.Value, "_now"));

        AssertNameAndPosition(activityTags[6], "Now", TagPosition.Start);
        AssertConvertExpression(AssertMemberExpression(activityTags[6].Value.Value, "Now"));

        static void AssertNameAndPosition(KeyValuePair<string, ActivityTagValue> activityTag, string name, TagPosition position)
        {
            Assert.Equal(name, activityTag.Key);
            Assert.Equal(position, activityTag.Value.Direction);
        }

        static void AssertConvertExpression(Expression? expression)
        {
            Assert.NotNull(expression);

            var unaryExpression = Assert.IsType<UnaryExpression>(expression);

            Assert.Equal(ExpressionType.Convert, unaryExpression.NodeType);

            Assert.Equal(typeof(ActivityTagTestClass2), unaryExpression.Type);

            AssertMemberExpression(unaryExpression.Operand, "InvocationTarget");
        }

        static Expression? AssertMemberExpression(Expression expression, string name)
        {
            var memberExpression = Assert.IsAssignableFrom<MemberExpression>(expression);

            Assert.Equal(ExpressionType.MemberAccess, memberExpression.NodeType);

            Assert.Equal(name, memberExpression.Member.Name);

            return memberExpression.Expression;
        }

        static void AssertCallExpression(Expression expression, Expression invocation, int index)
        {
            var methodCallExpression = Assert.IsAssignableFrom<MethodCallExpression>(expression);

            Assert.Equal(invocation, methodCallExpression.Object);

            Assert.Equal(typeof(IInvocation).GetMethod("GetArgumentValue"), methodCallExpression.Method);

            Assert.Equal(index, Assert.IsType<int>(Assert.IsType<ConstantExpression>(
                Assert.Single(methodCallExpression.Arguments)).Value));
        }
    }
}
