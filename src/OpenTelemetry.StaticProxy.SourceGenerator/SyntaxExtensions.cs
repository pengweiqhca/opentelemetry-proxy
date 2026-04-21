using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenTelemetry.StaticProxy;

internal static class SyntaxExtensions
{
    public static bool Is(this AttributeSyntax attribute, string attributeName)
    {
        var name = attribute.Name.ToString();

        if (name.EndsWith("Attribute", StringComparison.Ordinal)) name = name[..^9];

        return name == attributeName || name.EndsWith("." + attributeName, StringComparison.Ordinal);
    }

    public static bool Is(this NameEqualsSyntax nameEquals, string name) =>
        nameEquals.Name.ToString().Equals(name, StringComparison.Ordinal);

    public static bool IsPublic(this MemberDeclarationSyntax member) => member.Modifiers.HasModifier("public");

    public static bool IsStatic(this MemberDeclarationSyntax member) => member.Modifiers.HasModifier("static");

    public static bool IsRef(this BaseParameterSyntax member) => member.Modifiers.HasModifier("ref");

    public static bool IsOut(this BaseParameterSyntax member) => member.Modifiers.HasModifier("out");

    public static bool IsAsync(this MethodDeclarationSyntax method) =>
        /*!method.IsVoid() && */method.Modifiers.HasModifier("async");

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
