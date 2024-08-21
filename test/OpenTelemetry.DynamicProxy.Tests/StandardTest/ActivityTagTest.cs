using Castle.DynamicProxy;
using OpenTelemetry.Proxy.StandardFiles;
using System.Linq.Expressions;

namespace OpenTelemetry.DynamicProxy.Tests.StandardTest;

public class ActivityTagTest
{
    [Fact]
    public void ActivityTagsTest()
    {
        var (inTags, outTags, returnTags) = TestHelper.GetActivityTags<ActivityTagTestClass1>(x => x.TestMethod1);

        Assert.Equal(new()
        {
            [new("abc")] = ("invocation.GetArgumentValue(0)"),
            [new("def")] = ("invocation.GetArgumentValue(1)")
        }, inTags);

        Assert.Empty(outTags);

        Assert.NotNull(returnTags);
        Assert.Equal(new("$returnvalue"), Assert.Single(returnTags.Item1));
    }

    [Fact]
    public void ReturnValueTest()
    {
        var (inTags, outTags, returnTags) =
            TestHelper.GetActivityTags<ActivityTagTestClass2>(x => x.InstanceMethod);

        Assert.Equal(new()
        {
            [new("a2")] = "invocation.GetArgumentValue(0)",
            [new("b")] = "invocation.GetArgumentValue(2)",
            [new("d")] = "invocation.GetArgumentValue(4)",
            [new("e")] = "invocation.GetArgumentValue(5)",
#if NETFRAMEWORK
            [new("_now")] = ("Convert(invocation.InvocationTarget)._now"),
#else
            [new("_now")] = ("Convert(invocation.InvocationTarget, ActivityTagTestClass2)._now"),
#endif
#if NETFRAMEWORK
            [new("Now")] = ("Convert(invocation.InvocationTarget).Now")
#else
            [new("Now")] = ("Convert(invocation.InvocationTarget, ActivityTagTestClass2).Now")
#endif
        }, inTags);

        Assert.Equal(new()
        {
            [new("c")] = ("invocation.GetArgumentValue(3)"),
            [new("d")] = ("invocation.GetArgumentValue(4)"),
        }, outTags);

        Assert.NotNull(returnTags);
        Assert.Equal(new("ghi"), Assert.Single(returnTags.Item1));
    }

    [Fact]
    public void TypeHaveTagsTest()
    {
        var (inTags, outTags, returnTags) =
            TestHelper.GetActivityTags<ActivityTagTestClass3>(x => x.InstanceMethod);

        Assert.Equal(new()
        {
            [new("abc")] = ("invocation.GetArgumentValue(0)"),
            [new("age")] = ("invocation.GetArgumentValue(1)"),
            [new("Abc")] = ("ActivityTagTestClass3.Abc"),
#if NETFRAMEWORK
            [new("_abc")] = ("Convert(invocation.InvocationTarget)._abc")
#else
            [new("_abc")] = ("Convert(invocation.InvocationTarget, ActivityTagTestClass3)._abc")
#endif
        }, inTags);

        Assert.Empty(outTags);
        Assert.Null(returnTags);
    }

    [Fact]
    public void TypeNoTagsTest()
    {
        var (inTags, outTags, returnTags) =
            TestHelper.GetActivityTags<ActivityTagTestClass4>(x => x.InstanceMethod);

        Assert.Equal(new()
        {
            [new("abc")] = ("invocation.GetArgumentValue(0)"),
            [new("def")] = ("invocation.GetArgumentValue(1)"),
            [new("Abc")] = ("ActivityTagTestClass4.Abc"),
#if NETFRAMEWORK
            [new("_abc")] = ("Convert(invocation.InvocationTarget)._abc")
#else
            [new("_abc")] = ("Convert(invocation.InvocationTarget, ActivityTagTestClass4)._abc")
#endif
        }, inTags);

        Assert.Empty(outTags);
        Assert.Null(returnTags);
    }

    [Fact]
    public void GetActivityTags()
    {
        var tags = new[] { "_now", "Now", "e", Guid.NewGuid().ToString("N") }.ToDictionary(x => new ActivityTag(x),
            x => x);

        var invocation = Expression.Parameter(typeof(IInvocation), "invocation");

        (IReadOnlyDictionary<ActivityTag, Expression> inTags, IReadOnlyDictionary<ActivityTag, Expression> outTags,
            var returnTags) = ActivityInvokerFactory.GetActivityTags(typeof(ActivityTagTestClass2),
            typeof(ActivityTagTestClass2).GetMethod(nameof(ActivityTagTestClass2.InstanceMethod))!, tags, invocation);

        Assert.NotNull(returnTags);
        Assert.Equal(new("ghi"), Assert.Single(returnTags.Item1));

        Assert.Equal(6, inTags.Count);
        Assert.Equal(2, outTags.Count);

        AssertCallExpression(Assert.Contains(new("a2"), inTags), invocation, 0);
        AssertCallExpression(Assert.Contains(new("b"), inTags), invocation, 2);
        AssertCallExpression(Assert.Contains(new("c"), outTags), invocation, 3);
        AssertCallExpression(Assert.Contains(new("d"), inTags), invocation, 4);
        AssertCallExpression(Assert.Contains(new("d"), outTags), invocation, 4);
        AssertCallExpression(Assert.Contains(new("e"), inTags), invocation, 5);
        AssertConvertExpression(AssertMemberExpression(Assert.Contains(new("_now"), inTags), "_now"));
        AssertConvertExpression(AssertMemberExpression(Assert.Contains(new("Now"), inTags), "Now"));

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
