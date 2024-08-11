using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenTelemetry.StaticProxy.SourceTransformer;

internal sealed class TypeMethods(
    ITypeContext context,
    string typeName,
    string typeFullName,
    TypeDeclarationSyntax typeNode)
{
    private readonly Dictionary<MethodDeclarationSyntax, IMethodContext> _methodContexts = [];

    public ITypeContext Context { get; set; } = context;

    public TypeDeclarationSyntax TypeNode { get; } = typeNode;

    public string TypeName { get; } = typeName;

    public string TypeFullName { get; } = typeFullName;

    public IReadOnlyDictionary<MethodDeclarationSyntax, IMethodContext> MethodContexts => _methodContexts;

    public void AddMethod(MethodDeclarationSyntax method, IMethodContext methodContext) =>
        _methodContexts.Add(method, methodContext);
}
