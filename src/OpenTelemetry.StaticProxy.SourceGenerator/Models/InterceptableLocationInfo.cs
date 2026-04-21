namespace OpenTelemetry.StaticProxy;

/// <summary>
/// Serializable location info for [InterceptsLocation] attribute.
/// Stores the version and data needed to emit the attribute without holding Roslyn symbols.
/// </summary>
internal readonly record struct InterceptableLocationInfo(
    int Version,
    string Data);
