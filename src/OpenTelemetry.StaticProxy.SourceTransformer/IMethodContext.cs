namespace OpenTelemetry.StaticProxy;

internal interface IMethodContext;

internal interface IMethodTagContext : IMethodContext
{
    HashSet<string> UnknownTag { get; }

    Dictionary<string, ActivityTagSource> InTags { get; }

    bool IsStatic { get; set; }
}

internal sealed class SuppressInstrumentationContext : IMethodContext;
