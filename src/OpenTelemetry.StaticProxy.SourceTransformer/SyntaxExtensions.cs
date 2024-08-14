using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections;
using System.Linq.Expressions;

namespace OpenTelemetry.StaticProxy.SourceTransformer;

internal static class SyntaxExtensions
{
    private static readonly Tuple<Type, Func<ExpressionSyntax, IEnumerable<ExpressionSyntax>>>? CollectionExpression;

    static SyntaxExtensions()
    {
        var type1 = typeof(ImplicitArrayCreationExpressionSyntax).Assembly.GetType(
            "Microsoft.CodeAnalysis.CSharp.Syntax.CollectionExpressionSyntax");

        var type2 = typeof(ImplicitArrayCreationExpressionSyntax).Assembly.GetType(
            "Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionElementSyntax");

        if (type1 == null || type2 == null) return;

        var property1 = type1.GetProperty("Elements");
        if (property1 == null || !typeof(IEnumerable).IsAssignableFrom(property1.PropertyType)) return;

        var property2 = type2.GetProperty("Expression");
        if (property2 == null || !typeof(ExpressionSyntax).IsAssignableFrom(property2.PropertyType)) return;

        var method1 = typeof(Enumerable).GetMethod(nameof(Enumerable.OfType))?.MakeGenericMethod(type2);
        var method2 = typeof(Queryable).GetMethods()
            .FirstOrDefault(m => m.Name == nameof(Queryable.AsQueryable) && m.IsGenericMethod)
            ?.MakeGenericMethod(type2);

        var method3 = typeof(Queryable).GetMethods().FirstOrDefault(m =>
                m.Name == nameof(Queryable.Select) &&
                m.GetParameters()[1].ParameterType.GetGenericArguments()[0].GetGenericArguments().Length == 2)
            ?.MakeGenericMethod(type2, typeof(ExpressionSyntax));

        if (method1 == null || method2 == null || method3 == null) return;

        var p1 = Expression.Parameter(typeof(ExpressionSyntax));
        var p2 = Expression.Parameter(type2);

        var lambdaExpression = Expression.Lambda(Expression.Property(p2, property2), p2);

        var queryable = Expression.Call(method2, Expression.Call(method1, Expression.Convert(Expression.Property(Expression.Convert(p1, type1), property1), typeof(IEnumerable))));

        var expression = Expression.Lambda<Func<ExpressionSyntax, IEnumerable<ExpressionSyntax>>>(Expression.Call(method3, queryable, Expression.Constant(lambdaExpression)), p1);

        /*arg.Expression is CollectionExpressionSyntax collection
            ? collection.Elements.OfType<ExpressionElementSyntax>().AsQueryable().Select(x => x.Expression)*/
        CollectionExpression = new(type1, expression.Compile());
    }

    public static IEnumerable<ExpressionSyntax> TryConvertCollectionExpression(this ExpressionSyntax expression) =>
        CollectionExpression != null && CollectionExpression.Item1.IsInstanceOfType(expression)
            ? CollectionExpression.Item2(expression)
            : [expression];

    public static bool Is(this AttributeSyntax attribute, string attributeName)
    {
        var name = attribute.Name.ToString();

        if (name.EndsWith("Attribute", StringComparison.Ordinal)) name = name[..^9];

        return name == attributeName || name.EndsWith("." + attributeName, StringComparison.Ordinal);
    }

    public static bool IsPublic(this MemberDeclarationSyntax member) => member.Modifiers.HasModifier("public");

    public static bool IsStatic(this MemberDeclarationSyntax member) => member.Modifiers.HasModifier("static");

    public static bool IsRef(this BaseParameterSyntax member) => member.Modifiers.HasModifier("ref");

    public static bool IsOut(this BaseParameterSyntax member) => member.Modifiers.HasModifier("out");

    public static bool IsAsync(this MethodDeclarationSyntax method) =>
        !method.IsVoid() && method.Modifiers.HasModifier("async");

    public static bool IsVoid(this MethodDeclarationSyntax method) =>
        method.ReturnType is PredefinedTypeSyntax predefinedType &&
        predefinedType.Keyword.IsKind(SyntaxKind.VoidKeyword);

    private static bool HasModifier(this SyntaxTokenList modifiers, string modifier) =>
        modifiers.Any(x => x.ToString() == modifier);

    public static TypeDeclarationSyntax? GetDeclaringType(this SyntaxNode? node)
    {
        if (node == null) return null;

        do
        {
            node = node.Parent;

            if (node is TypeDeclarationSyntax type) return type;
        }
        while (node != null);

        return null;
    }

    public static string GetTypeFullName(this SyntaxNode node)
    {
        var fullName = "";

        while (true)
        {
            switch (node)
            {
                case TypeDeclarationSyntax type:
                    fullName = string.IsNullOrWhiteSpace(fullName)
                        ? type.GetTypeName()
                        : type.GetTypeName() + "+" + fullName;

                    break;
                case BaseNamespaceDeclarationSyntax ns:
                    fullName = ns.Name + "." + fullName;
                    break;
                default:
                    return fullName;
            }

            node = node.Parent!;
        }
    }

    /// <param name="type"></param>
    /// <param name="format">0: Open generic type name, Test`1; 1: Open generic type, Test&lt;&gt;; 2: generic type, Test&lt;T&gt;.</param>
    public static string GetTypeName(this TypeDeclarationSyntax type, int format = 0)
    {
        var typeName = type.Identifier.ToString();

        return type.TypeParameterList is { Parameters: { Count: > 0 } parameters }
            ? format switch
            {
                0 => typeName + "`" + parameters.Count,
                1 => typeName + "<" + new string(',', parameters.Count - 1) + ">",
                _ => typeName + "<" + string.Join(", ", parameters.Select(x => x.Identifier)) + ">"
            }
            : typeName;
    }

    public static string GetMethodName(this MethodDeclarationSyntax method)
    {
        var name = method.Identifier.ToString();

        while (method.Parent is MethodDeclarationSyntax m)
        {
            method = m;

            name = method.Identifier + "+" + name;
        }

        return name;
    }

    public static SyntaxToken WithWhiteSpace(this SyntaxToken node) =>
        node.WithLeadingWhiteSpace().WithTrailingWhiteSpace();

    public static SyntaxToken WithLeadingWhiteSpace(this SyntaxToken node) =>
        node.WithLeadingTrivia(SyntaxFactory.Whitespace(" "));

    public static SyntaxToken WithTrailingWhiteSpace(this SyntaxToken node) =>
        node.WithTrailingTrivia(SyntaxFactory.Whitespace(" "));

    public static SyntaxToken WithNewLine(this SyntaxToken node) =>
        node.WithTrailingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine));

    public static SyntaxToken WithNewLine(this SyntaxToken node, int width) =>
        node.WithLeadingTrivia(SyntaxFactory.Whitespace(new(' ', width)))
            .WithTrailingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine));

    public static TSyntax WithWhiteSpace<TSyntax>(this TSyntax node) where TSyntax : SyntaxNode =>
        node.WithLeadingWhiteSpace().WithTrailingWhiteSpace();

    public static TSyntax WithLeadingWhiteSpace<TSyntax>(this TSyntax node) where TSyntax : SyntaxNode =>
        node.WithLeadingTrivia(SyntaxFactory.Whitespace(" "));

    public static TSyntax WithTrailingWhiteSpace<TSyntax>(this TSyntax node) where TSyntax : SyntaxNode =>
        node.WithTrailingTrivia(SyntaxFactory.Whitespace(" "));

    public static TSyntax WithNewLine<TSyntax>(this TSyntax node) where TSyntax : SyntaxNode =>
        node.WithTrailingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine));

    public static TSyntax WithNewLine<TSyntax>(this TSyntax node, int width) where TSyntax : SyntaxNode =>
        node.WithLeadingTrivia(SyntaxFactory.Whitespace(new(' ', width)))
            .WithTrailingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine));

    public static SyntaxToken RestoreLineNumber(this SyntaxToken node, ILineNumber line)
    {
        if (node.LeadingTrivia.Any(x => x.IsKind(SyntaxKind.LineDirectiveTrivia))) return node;

        var newLeadingTrivia = SyntaxFactory.Trivia(SyntaxFactory.LineDirectiveTrivia(
                SyntaxFactory.Literal(line.Line).WithLeadingWhiteSpace(),
                SyntaxFactory.Literal(line.File).WithLeadingWhiteSpace(), true)
            .WithTrailingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine)));

        for (var index = node.LeadingTrivia.Count - 1; index >= 0; index--)
            if (node.LeadingTrivia[index].IsKind(SyntaxKind.WhitespaceTrivia))
                return node.WithLeadingTrivia(node.LeadingTrivia.Take(index)
                    .Append(newLeadingTrivia).Union(node.LeadingTrivia.Skip(index)));

        return node.WithLeadingTrivia(node.LeadingTrivia.Append(newLeadingTrivia));
    }

    /// <summary>Restore raw line number.</summary>
    public static TSyntax RestoreLineNumber<TSyntax>(this TSyntax node, ILineNumber line) where TSyntax : SyntaxNode
    {
        if (node.HaveLineNumber()) return node;

        var newLeadingTrivia = SyntaxFactory.Trivia(SyntaxFactory.LineDirectiveTrivia(
                SyntaxFactory.Literal(line.Line).WithLeadingWhiteSpace(),
                SyntaxFactory.Literal(line.File).WithLeadingWhiteSpace(), true)
            .WithTrailingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine)));

        var syntaxTriviaList = node.GetLeadingTrivia();

        for (var index = syntaxTriviaList.Count - 1; index >= 0; index--)
            if (syntaxTriviaList[index].IsKind(SyntaxKind.WhitespaceTrivia))
                return node.WithLeadingTrivia(syntaxTriviaList.Take(index)
                    .Append(newLeadingTrivia).Union(syntaxTriviaList.Skip(index)));

        return node.WithLeadingTrivia(syntaxTriviaList.Append(newLeadingTrivia));
    }

    /// <summary>Hidden raw line number.</summary>
    public static TSyntax HiddenLineNumber<TSyntax>(this TSyntax node) where TSyntax : SyntaxNode
    {
        if (node.HaveLineNumber()) return node;

        var newLeadingTrivia = SyntaxFactory.Trivia(SyntaxFactory.LineDirectiveTrivia(
                SyntaxFactory.Token(SyntaxKind.HiddenKeyword).WithLeadingWhiteSpace(), true)
            .WithTrailingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine)));

        var syntaxTriviaList = node.GetLeadingTrivia();

        for (var index = syntaxTriviaList.Count - 1; index >= 0; index--)
            if (syntaxTriviaList[index].IsKind(SyntaxKind.WhitespaceTrivia))
                return node.WithLeadingTrivia(syntaxTriviaList.Take(index)
                    .Append(newLeadingTrivia).Union(syntaxTriviaList.Skip(index)));

        return node.WithLeadingTrivia(syntaxTriviaList.Append(newLeadingTrivia));
    }

    public static bool HaveLineNumber(this SyntaxNode node) =>
        node.GetLeadingTrivia().Any(x => x.IsKind(SyntaxKind.LineDirectiveTrivia));

    public static ILineNumber GetLineNumber(this SyntaxNode node, int offset = 1) => new LineNumber(
        node.SyntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + offset, node.SyntaxTree.FilePath);

    public static int GetColumnNumber(this SyntaxNode node) =>
        node.SyntaxTree.GetLineSpan(node.Span).StartLinePosition.Character;

    public static ILineNumber GetLineNumber(this SyntaxToken node, SyntaxTree tree, int offset = 1) => new LineNumber(
        tree.GetLineSpan(node.Span).StartLinePosition.Line + offset, tree.FilePath);

    public static int GetIndent(this SyntaxNode node) => node.GetLeadingTrivia()
        .FirstOrDefault(x => x.IsKind(SyntaxKind.WhitespaceTrivia)).FullSpan.Length;

    public static BlockSyntax WithNewLine(this BlockSyntax block) => block
        .WithOpenBraceToken(block.OpenBraceToken.WithNewLine())
        .WithOpenBraceToken(block.OpenBraceToken.WithNewLine());

    public static bool IsVoid(this IMethodSymbol methodSymbol, SemanticModel semanticModel, int position)
    {
        if (!methodSymbol.IsAsync) return methodSymbol.ReturnsVoid;

        // otherwise: needs valid GetAwaiter
        var potentialGetAwaiters = semanticModel.LookupSymbols(position,
            container: methodSymbol.ReturnType.OriginalDefinition,
            name: WellKnownMemberNames.GetAwaiter,
            includeReducedExtensionMethods: true);

        return potentialGetAwaiters.OfType<IMethodSymbol>().All(getAwaiter =>
            getAwaiter.Parameters.Length != 0 || GetGetResultMethod(getAwaiter) is not { ReturnsVoid: false });
    }

    private static IMethodSymbol? GetGetResultMethod(IMethodSymbol getAwaiter)
    {
        if (getAwaiter.ReturnsVoid) return null;

        if (!getAwaiter.ReturnType.GetMembers().OfType<IPropertySymbol>().Any(p =>
                p.Name == WellKnownMemberNames.IsCompleted && p.Type.SpecialType == SpecialType.System_Boolean &&
                p.GetMethod != null)) return null;

        var methods = getAwaiter.ReturnType.GetMembers().OfType<IMethodSymbol>().ToArray();

        return methods.Any(x =>
            x.Name == WellKnownMemberNames.OnCompleted &&
            x is { ReturnsVoid: true, Parameters: [{ Type.TypeKind: TypeKind.Delegate }] })
            ? methods.FirstOrDefault(m => m.Name == WellKnownMemberNames.GetResult && m.Parameters.Length == 0)
            : null;
    }
}
