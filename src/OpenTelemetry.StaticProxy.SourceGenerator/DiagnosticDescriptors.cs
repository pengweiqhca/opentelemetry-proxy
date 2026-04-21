using Microsoft.CodeAnalysis;

namespace OpenTelemetry.StaticProxy;

/// <summary>
/// Centralized diagnostic descriptors for the Static Proxy Source Generator.
/// </summary>
internal static class DiagnosticDescriptors
{
#pragma warning disable RS2008

    /// <summary>
    /// OTSP001: Unrecognized attribute argument expression.
    /// </summary>
    internal static readonly DiagnosticDescriptor UnrecognizedAttributeArg = new(
        "OTSP001",
        "Unrecognized attribute argument",
        "Unrecognized attribute argument expression '{0}'",
        "OpenTelemetry.StaticProxy",
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// OTSP002: Attribute argument value is null when non-null is expected.
    /// </summary>
    internal static readonly DiagnosticDescriptor InvalidAttributeArgValue = new(
        "OTSP002",
        "Invalid attribute argument value",
        "Expected attribute argument is not null, or remove this argument value",
        "OpenTelemetry.StaticProxy",
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// OTSP003: Attribute argument type does not match expected type.
    /// </summary>
    internal static readonly DiagnosticDescriptor InvalidAttributeArgType = new(
        "OTSP003",
        "Invalid attribute argument type",
        "Expected attribute argument type is '{0}' but found '{1}'",
        "OpenTelemetry.StaticProxy",
        DiagnosticSeverity.Error,
        true);

#pragma warning restore RS2008
}
