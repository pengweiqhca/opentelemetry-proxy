using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenTelemetry.StaticProxy;

internal sealed class TypeSyntaxContext
{
    public List<TypeDeclarationSyntax> Types { get; } = [];

    public MethodSyntaxContexts Methods { get; } = [];

    public Dictionary<string, MemberType> PropertyOrField { get; } = [];
}

internal sealed class TypeSyntaxContexts
{
    public Dictionary<TypeDeclarationSyntax, string> FullNames { get; } = [];

    public Dictionary<string, TypeSyntaxContext> Types { get; } = [];

    public void VisitTypeDeclaration(TypeDeclarationSyntax node)
    {
        var fullName = node.GetTypeFullName();

        if (!Types.TryGetValue(fullName, out var typeContext)) Types.Add(fullName, typeContext = new());

        typeContext.Types.Add(node);

        FullNames[node] = fullName;
    }

    public void VisitMemberDeclaration(IEnumerable<SyntaxToken> identifiers, MemberDeclarationSyntax node)
    {
        if (node.Parent is not TypeDeclarationSyntax typeSyntax) return;

        var isStatic = node.IsStatic();

        foreach (var identifier in identifiers)
            Types[FullNames[typeSyntax]].PropertyOrField[identifier.ToString()] =
                new(isStatic, node is PropertyDeclarationSyntax);
    }

    public void AddMethod(TypeDeclarationSyntax type, MethodDeclarationSyntax method, IMethodSymbol? methodSymbol)
    {
        var methodSyntaxContexts = Types[FullNames[type]].Methods;

        if (methodSymbol == null) methodSyntaxContexts[method] = null;
        else if (methodSymbol.PartialImplementationPart != null)
        {
            if (methodSyntaxContexts.I2D.TryGetValue(methodSymbol.PartialImplementationPart, out var impl))
                methodSyntaxContexts[impl] = method;
            else methodSyntaxContexts.I2D[methodSymbol] = method;
        }
        else if (methodSymbol.PartialDefinitionPart != null)
        {
            if (methodSyntaxContexts.I2D.TryGetValue(methodSymbol.PartialDefinitionPart, out var def))
                methodSyntaxContexts[method] = def;
            else methodSyntaxContexts.I2D[methodSymbol] = method;
        }
        else methodSyntaxContexts[method] = null;
    }
}

// Implementation, Definition
internal sealed class MethodSyntaxContexts : Dictionary<MethodDeclarationSyntax, MethodDeclarationSyntax?>
{
    public Dictionary<IMethodSymbol, MethodDeclarationSyntax> I2D { get; } = [];
}
