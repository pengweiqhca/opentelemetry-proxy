namespace OpenTelemetry.Proxy.Models;

/// <summary>
/// Immutable metadata for a single call site that should be intercepted.
/// All data is serializable (no Roslyn symbols) for Incremental Generator caching.
/// </summary>
internal readonly record struct InterceptCallSite(
    MethodMetadata Target,
    InterceptableLocationInfo Location,
    ResolvedMethodInfo ResolvedMethod);
