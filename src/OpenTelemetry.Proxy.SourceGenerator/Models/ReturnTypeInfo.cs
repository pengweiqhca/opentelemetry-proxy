namespace OpenTelemetry.Proxy.Models;

/// <summary>
/// Immutable metadata for a method's return type.
/// </summary>
internal readonly record struct ReturnTypeInfo(
    string TypeFullName,
    bool IsVoid,
    bool IsAsync,
    bool IsTask,
    bool IsValueTask);
