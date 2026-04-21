namespace OpenTelemetry.StaticProxy;

/// <summary>
/// Immutable metadata for a single Activity tag, used in Incremental Generator caching.
/// </summary>
internal readonly record struct TagMetadata(
    string TagName,
    string SourceName,
    TagSource Source,
    string? Expression);
