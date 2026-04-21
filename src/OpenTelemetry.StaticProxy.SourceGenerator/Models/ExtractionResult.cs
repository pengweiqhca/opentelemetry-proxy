namespace OpenTelemetry.StaticProxy;

/// <summary>
/// Result of extracting TypeMetadata, including any diagnostics encountered.
/// When HasErrors is true, the type should be skipped during code generation.
/// </summary>
internal readonly record struct TypeExtractionResult(
    TypeMetadata Metadata,
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    public bool HasErrors => Diagnostics.Length > 0;
}

/// <summary>
/// Result of extracting MethodMetadata, including any diagnostics encountered.
/// When HasErrors is true, the method should be skipped during code generation.
/// </summary>
internal readonly record struct MethodExtractionResult(
    MethodMetadata Metadata,
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    public bool HasErrors => Diagnostics.Length > 0;
}

/// <summary>
/// Result of extracting ActivityName metadata (can be either type or method level).
/// </summary>
internal readonly record struct ActivityNameExtractionResult(
    object? Item,
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    public bool HasErrors => Diagnostics.Length > 0;
}
