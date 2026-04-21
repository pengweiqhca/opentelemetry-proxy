namespace OpenTelemetry.Proxy.Models;

/// <summary>
/// Immutable metadata for a method parameter.
/// </summary>
internal readonly record struct ParameterInfo(
    string Name,
    string TypeFullName,
    RefKind RefKind);

/// <summary>
/// The ref kind of a parameter.
/// </summary>
internal enum RefKind
{
    None,
    Ref,
    Out,
    In,
    RefReadonlyParameter
}
