namespace OpenTelemetry.Proxy.Models;

/// <summary>
/// Immutable metadata for a method within a type declaration (used in TypeMetadata).
/// Captures basic info about whether the method has explicit attributes.
/// </summary>
internal readonly record struct MethodInfo(
    string MethodName,
    bool HasActivityAttribute,
    bool HasActivityNameAttribute,
    bool HasNonActivityAttribute,
    bool IsAsync,
    bool IsPublic);
