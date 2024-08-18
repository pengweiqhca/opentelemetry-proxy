namespace OpenTelemetry.StaticProxy.SourceTransformer;

internal interface IMethodContext;

internal interface IMethodTagContext : IMethodContext
{
    HashSet<ActivityTag> UnknownTag { get; }

    Dictionary<ActivityTag, ActivityTagSource> InTags { get; }

    bool IsStatic { get; set; }
}

internal sealed class SuppressInstrumentationContext : IMethodContext;
