using System.Linq.Expressions;
using System.Reflection;

namespace OpenTelemetry.DynamicProxy.Tests;

public class DefaultExpressionParserTest
{
    [Fact]
    public void ClassTest()
    {
        var expression = Parse(Expression.Parameter(typeof(A), "this"), "$.B?.C");

        Assert.Equal("""
                     .Block() {
                         $var1 = $this.B;
                         .If ($var1 == null) {
                             null
                         } .Else {
                             $var1.C
                         }
                     }
                     """, ToString(expression));
    }

    [Fact]
    public void ThisNullableTest()
    {
        var expression = Parse(Expression.Parameter(typeof(A), "this"), "$?.B?.C");

        Assert.Equal("""
                     .If ($this == null) {
                         null
                     } .Else {
                         .Block() {
                             $var1 = $this.B;
                             .If ($var1 == null) {
                                 null
                             } .Else {
                                 $var1.C
                             }
                         }
                     }
                     """, ToString(expression));
    }

    [Fact]
    public void StructTest()
    {
        var expression = Parse(Expression.Parameter(typeof(A), "this"), "$.B.C?.D.Name");

        Assert.Equal("""
                     .Block() {
                         $var1 = ($this.B).C;
                         .If ($var1 == null) {
                             null
                         } .Else {
                             ($var1.D).Name
                         }
                     }
                     """, ToString(expression));
    }

    [Fact]
    public void MethodTest()
    {
        var expression = Parse(Expression.Parameter(typeof(A), "this"), "$.B.C?.GetD().Name");

        Assert.Equal("""
                     .Block() {
                         $var1 = ($this.B).C;
                         .If ($var1 == null) {
                             null
                         } .Else {
                             (.Call $var1.GetD()).Name
                         }
                     }
                     """, ToString(expression));
    }

    [Fact]
    public void StructNullableTest()
    {
        var expression = Parse(Expression.Parameter(typeof(A), "this"), "$.B.C?.D.Age");

        Assert.Equal("""
                     .Block() {
                         $var1 = ($this.B).C;
                         .If ($var1 == null) {
                             null
                         } .Else {
                             (System.Object)($var1.D).Age
                         }
                     }
                     """, ToString(expression));

        expression = Parse(Expression.Parameter(typeof(A), "this"), "$.B.C?.D.Code");

        Assert.Equal("""
                     .Block() {
                         $var1 = ($this.B).C;
                         .If ($var1 == null) {
                             null
                         } .Else {
                             (System.Object)($var1.D).Code
                         }
                     }
                     """, ToString(expression));
    }

    [Fact]
    public void InvalidExpressionTest()
    {
        Assert.Throws<ArgumentException>(() => Parse(Expression.Parameter(typeof(A), "this"), "$a.B.C"));
        Assert.Throws<ArgumentException>(() => Parse(Expression.Parameter(typeof(A), "this"), "$a?.B.C"));
        Assert.Throws<ArgumentException>(() => Parse(Expression.Parameter(typeof(A), "this"), "$.B.C.D?.Age"));
        Assert.Throws<NotSupportedException>(() => Parse(Expression.Parameter(typeof(A), "this"), "$.B.C?.GetD(d).Name"));
    }

    private static Expression Parse(Expression @this, string expression) =>
        new DefaultExpressionParser().Parse(@this, expression);

    private static string? ToString(Expression expression) =>
        typeof(Expression).GetProperty("DebugView", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(expression) as string;

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
