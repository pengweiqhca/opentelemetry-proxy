namespace OpenTelemetry.StaticProxy.SourceTransformer;

internal sealed class ProxyRewriterContext
{
    public bool AssemblyHasAddedAttribute { get; set; }

    public HashSet<string> TypeHasAddedAttribute { get; } = [];
}
