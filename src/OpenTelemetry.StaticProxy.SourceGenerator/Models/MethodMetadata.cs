namespace OpenTelemetry.StaticProxy;

/// <summary>
/// Immutable metadata for a method that needs interception,
/// used in Incremental Generator caching.
/// </summary>
internal readonly record struct MethodMetadata(
    string ContainingTypeFullName,
    string MethodName,
    string MethodSymbolKey,
    MethodMode Mode,
    string? ActivityName,
    string? Kind,
    bool SuppressInstrumentation,
    bool AdjustStartTime,
    bool IsStatic,
    bool IsVoid,
    bool IsAsync,
    EquatableArray<TagMetadata> InTags,
    EquatableArray<TagMetadata> OutTags,
    EquatableArray<ParameterInfo> Parameters,
    ReturnTypeInfo ReturnType);
