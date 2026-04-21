using Microsoft.CodeAnalysis;

namespace OpenTelemetry.Proxy.Models;

/// <summary>
/// Immutable, equatable representation of a diagnostic to be reported.
/// Stores the descriptor and location info so it can be used in the Incremental Generator pipeline.
/// </summary>
internal readonly record struct DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    LocationInfo Location,
    EquatableArray<string> MessageArgs);

/// <summary>
/// Serializable location info for diagnostics (Location is not equatable).
/// </summary>
internal readonly record struct LocationInfo(
    string FilePath,
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter);
