namespace OpenTelemetry.Proxy.Models;

/// <summary>
/// Immutable metadata for a type annotated with [ActivitySource] or [ActivityName],
/// used in Incremental Generator caching.
/// </summary>
internal readonly record struct TypeMetadata(
    string TypeFullName,
    string ActivitySourceName,
    string Kind,
    bool IncludeAllMethods,
    bool SuppressInstrumentation,
    EquatableArray<TagMetadata> TypeTags,
    EquatableArray<MemberInfo> Members,
    EquatableArray<MethodInfo> Methods);
