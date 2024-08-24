namespace OpenTelemetry.StaticProxy;

internal sealed class ProxyRewriterContext
{
    public bool AssemblyHasAddedAttribute { get; set; }

    public HashSet<string> TypeHasAddedAttribute { get; } = [];
}
