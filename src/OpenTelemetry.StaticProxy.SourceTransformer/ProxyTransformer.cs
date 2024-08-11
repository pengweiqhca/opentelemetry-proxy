using Metalama.Compiler;
using Microsoft.CodeAnalysis;

namespace OpenTelemetry.StaticProxy.SourceTransformer;

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

        var proxyRewriterContext = new ProxyRewriterContext();

        foreach (var tree in methods.SelectMany(x => x.MethodContexts.Select(m => new
                 {
                     m.Key.SyntaxTree,
                     TypeMethods = x,
                     Method = m.Key,
                     MethodContext = m.Value
                 })).GroupBy(x => x.SyntaxTree))
        {
            var rewriter = new ProxyRewriter(proxyRewriterContext,
                tree.Select(x => x.TypeMethods).Distinct().ToDictionary(x => x.TypeNode, x => x.Context),
                tree.ToDictionary(x => x.Method, x => x.MethodContext));

            context.ReplaceSyntaxTree(
                tree.Key,
                tree.Key.WithRootAndOptions(rewriter.Visit(tree.Key.GetRoot()), tree.Key.Options));
        }
    }
}
