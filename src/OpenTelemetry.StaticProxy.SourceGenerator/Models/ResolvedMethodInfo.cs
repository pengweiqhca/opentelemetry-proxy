namespace OpenTelemetry.StaticProxy;

/// <summary>
/// Serializable info about the resolved method at a call site.
/// Captures the information needed for code generation without holding Roslyn IMethodSymbol.
/// </summary>
internal readonly record struct ResolvedMethodInfo(
    string ContainingTypeFullName,
    string MethodName,
    bool IsStatic,
    bool IsExtensionMethod,
    EquatableArray<string> TypeArguments,
    EquatableArray<GenericConstraintInfo> GenericConstraints,
    EquatableArray<ParameterInfo> Parameters,
    ReturnTypeInfo ReturnType);

/// <summary>
/// Serializable info about a generic type parameter's constraints.
/// </summary>
internal readonly record struct GenericConstraintInfo(
    string TypeParameterName,
    string ConstraintClause);
