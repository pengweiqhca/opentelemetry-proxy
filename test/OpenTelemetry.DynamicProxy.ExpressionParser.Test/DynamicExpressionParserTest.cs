using System.Linq.Expressions;

namespace OpenTelemetry.DynamicProxy.ExpressionParser.Test;

public class DynamicExpressionParserTest
{
    [Fact]
    public void Test()
    {
        var p = Expression.Parameter(typeof(A));

        var expression = new DynamicExpressionParser().Parse(p, "$.B.C.GetD().Name");

        var a = new A() { B = new() { C = new() { D = new() { Name = "Name" } } } };

        Assert.Equal(a.B.C.GetD().Name, Expression.Lambda<Func<A, object>>(expression, p).Compile()(a));
    }

#nullable disable
    private class A
    {
        public B B { get; set; }
    }

    private class B
    {
        public C C { get; set; }
    }

    private class C
    {
        public D D { get; set; }

        public D GetD() => D;

        public D GetD(D d) => d;
    }

    private struct D
    {
        public string Name { get; set; }

        public int Age { get; set; }

        public int? Code { get; set; }
    }
}
