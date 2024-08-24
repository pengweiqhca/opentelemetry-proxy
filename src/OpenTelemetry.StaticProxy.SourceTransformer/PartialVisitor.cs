using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenTelemetry.StaticProxy;

internal sealed class PartialVisitor(Compilation compilation) : CSharpSyntaxRewriter
{
    public TypeSyntaxContexts Types { get; } = new();

    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        Types.VisitTypeDeclaration(node);

        return base.VisitClassDeclaration(node);
    }

    public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
    {
        Types.VisitTypeDeclaration(node);

        return base.VisitStructDeclaration(node);
    }

    public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        Types.VisitTypeDeclaration(node);

        return base.VisitRecordDeclaration(node);
    }

    public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        Types.VisitTypeDeclaration(node);

        return base.VisitInterfaceDeclaration(node);
    }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (node.GetDeclaringType() is { } typeSyntax)
            Types.AddMethod(typeSyntax, node,
                compilation.GetSemanticModel(node.SyntaxTree).GetDeclaredSymbol(node));

        return base.VisitMethodDeclaration(node);
    }

    public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        Types.VisitMemberDeclaration(node.Declaration.Variables.Select(x => x.Identifier), node);

        return base.VisitFieldDeclaration(node);
    }

    public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        Types.VisitMemberDeclaration([node.Identifier], node);

        return base.VisitPropertyDeclaration(node);
    }
}
