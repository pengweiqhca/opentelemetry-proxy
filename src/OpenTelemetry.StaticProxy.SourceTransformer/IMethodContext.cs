namespace OpenTelemetry.StaticProxy.SourceTransformer;

internal interface IMethodContext;

internal interface IMethodTagContext : IMethodContext
{
    Dictionary<ActivityTag, string> UnknownTag { get; }

    Dictionary<ActivityTag, ActivityTagSource> InTags { get; }

    bool IsStatic { get; set; }
}

internal sealed class SuppressInstrumentationContext : IMethodContext;
