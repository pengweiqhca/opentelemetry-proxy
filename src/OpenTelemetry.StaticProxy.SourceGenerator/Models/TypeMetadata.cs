namespace OpenTelemetry.StaticProxy;

/// <summary>
/// Immutable metadata for a type annotated with [ActivitySource] or [ActivityName],
/// used in Incremental Generator caching.
/// </summary>
internal readonly record struct TypeMetadata(
    string TypeFullName,
    string ActivitySourceName,
    string Kind,
    bool IncludeNonAsyncStateMachineMethod,
    bool SuppressInstrumentation,
    EquatableArray<TagMetadata> TypeTags,
    EquatableArray<MemberInfo> Members,
    EquatableArray<MethodInfo> Methods);
