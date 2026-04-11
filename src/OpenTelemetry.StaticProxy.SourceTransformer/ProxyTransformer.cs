using Metalama.Compiler;
using Microsoft.CodeAnalysis;

namespace OpenTelemetry.StaticProxy;

[Transformer]
public class ProxyTransformer : ISourceTransformer
{
    public void Execute(TransformerContext context)
    {
        var list = new List<Diagnostic>();

        var methods = ProxyVisitor.GetProxyMethods(context.Compilation, list.Add);

        if (list.Count > 0)
        {
            list.ForEach(context.ReportDiagnostic);

            return;
        }

        foreach (var (old, @new) in Rewrite(methods))
            context.ReplaceSyntaxTree(old, old.WithRootAndOptions(@new, old.Options));
    }

    internal static IEnumerable<Tuple<SyntaxTree, SyntaxNode>> Rewrite(IReadOnlyCollection<TypeMethods> methods)
    {
        var proxyRewriterContext = new ProxyRewriterContext();

        foreach (var tree in methods.SelectMany(x => x.MethodContexts.Select(m => new
                 {
                     m.Key.SyntaxTree,
                     TypeMethods = x,
                     Method = m.Key,
                     MethodContext = m.Value
                 })).GroupBy(x => x.SyntaxTree))
            yield return Tuple.Create(tree.Key, new ProxyRewriter(proxyRewriterContext,
                tree.Select(x => x.TypeMethods).Distinct().ToDictionary(x => x.TypeNode, x => x.Context),
                tree.ToDictionary(x => x.Method, x => x.MethodContext)).Visit(tree.Key.GetRoot()));
    }
}
