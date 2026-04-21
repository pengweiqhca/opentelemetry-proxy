namespace OpenTelemetry.Proxy.Models;

/// <summary>
/// Immutable metadata for a type member (field or property).
/// </summary>
internal readonly record struct MemberInfo(
    string Name,
    string TypeFullName,
    bool IsStatic,
    bool IsProperty);
