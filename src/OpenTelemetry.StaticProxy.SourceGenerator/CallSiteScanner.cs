using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenTelemetry.StaticProxy;

internal static class CallSiteScanner
{
    /// <summary>
    /// Scan all syntax trees in the compilation for invocations that match target methods,
    /// and build InterceptCallSite records with interception location info.
    /// </summary>
    public static ImmutableArray<InterceptCallSite> ScanCallSites(
        Compilation compilation,
        ImmutableArray<MethodMetadata> targetMethods,
        ImmutableArray<TypeMetadata> types,
        CancellationToken ct)
    {
        if (targetMethods.IsDefaultOrEmpty && types.IsDefaultOrEmpty)
            return [];

        // Build a lookup from MethodSymbolKey -> MethodMetadata for explicit methods
        var explicitMethodLookup = new Dictionary<string, MethodMetadata>(StringComparer.Ordinal);
        foreach (var method in targetMethods)
        {
            if (!string.IsNullOrEmpty(method.MethodSymbolKey))
                explicitMethodLookup[method.MethodSymbolKey] = method;
        }

        // Build a lookup from type full name -> TypeMetadata for auto-include filtering
        var typeLookup = new Dictionary<string, TypeMetadata>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            if (!string.IsNullOrEmpty(type.TypeFullName))
                typeLookup[type.TypeFullName] = type;
        }

        var results = ImmutableArray.CreateBuilder<InterceptCallSite>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();

            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(ct);

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                ct.ThrowIfCancellationRequested();

                var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
                if (symbolInfo.Symbol is not IMethodSymbol invokedMethod) continue;

                // Try to match against explicit target methods or auto-included methods
                var matchResult = TryMatchMethod(invokedMethod, explicitMethodLookup, typeLookup);
                if (matchResult == null) continue;

                // Get the interceptable location
                var location = semanticModel.GetInterceptableLocation(invocation, ct);
                if (location == null) continue;

                var locationInfo = new InterceptableLocationInfo(location.Version, location.Data);
                var resolvedMethod = BuildResolvedMethodInfo(invokedMethod);

                results.Add(new(matchResult.Value, locationInfo, resolvedMethod));
            }
        }

        return results.ToImmutable();
    }

    /// <summary>
    /// Try to match an invoked method symbol against explicit target methods or auto-included type methods.
    /// Returns the MethodMetadata if matched, null otherwise.
    /// </summary>
    private static MethodMetadata? TryMatchMethod(
        IMethodSymbol invokedMethod,
        Dictionary<string, MethodMetadata> explicitMethodLookup,
        Dictionary<string, TypeMetadata> typeLookup)
    {
        // 1. Try direct match via MethodSymbolKey
        var key = MetadataExtractor.BuildMethodSymbolKey(invokedMethod);
        if (explicitMethodLookup.TryGetValue(key, out var directMatch))
            return directMatch;

        // 2. If the method is from an original definition (generic), try matching that
        if (!SymbolEqualityComparer.Default.Equals(invokedMethod, invokedMethod.OriginalDefinition))
        {
            var originalKey = MetadataExtractor.BuildMethodSymbolKey(invokedMethod.OriginalDefinition);
            if (explicitMethodLookup.TryGetValue(originalKey, out var originalMatch))
                return originalMatch;
        }

        // 3. Try interface/abstract method matching — walk the method's interface implementations
        //    and overridden methods to see if any of those are in our target set
        var interfaceMatch = TryMatchInterfaceOrAbstractMethod(invokedMethod, explicitMethodLookup);
        if (interfaceMatch != null)
            return interfaceMatch;

        // 4. Try auto-include: check if the method belongs to a type in our types collection
        //    and should be auto-included based on IncludeNonAsyncStateMachineMethod rules
        return TryAutoIncludeMethod(invokedMethod, typeLookup);
    }

    /// <summary>
    /// Try to match an invoked method against interface or abstract method definitions
    /// that are in our explicit target set.
    /// </summary>
    private static MethodMetadata? TryMatchInterfaceOrAbstractMethod(
        IMethodSymbol invokedMethod,
        Dictionary<string, MethodMetadata> explicitMethodLookup)
    {
        // Check if the invoked method itself is an interface/abstract method
        if (invokedMethod.ContainingType is { TypeKind: TypeKind.Interface } or { IsAbstract: true })
        {
            var key = MetadataExtractor.BuildMethodSymbolKey(invokedMethod);
            if (explicitMethodLookup.TryGetValue(key, out var match))
                return match;
        }

        // Check interface implementations of the containing type
        if (invokedMethod.ContainingType != null)
        {
            foreach (var iface in invokedMethod.ContainingType.AllInterfaces)
            {
                foreach (var ifaceMember in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    var impl = invokedMethod.ContainingType.FindImplementationForInterfaceMember(ifaceMember);
                    if (impl != null && SymbolEqualityComparer.Default.Equals(impl, invokedMethod))
                    {
                        var ifaceKey = MetadataExtractor.BuildMethodSymbolKey(ifaceMember);
                        if (explicitMethodLookup.TryGetValue(ifaceKey, out var ifaceMatch))
                            return ifaceMatch;
                    }
                }
            }
        }

        // Walk the overridden chain for abstract/virtual methods
        var overridden = invokedMethod.OverriddenMethod;
        while (overridden != null)
        {
            var overriddenKey = MetadataExtractor.BuildMethodSymbolKey(overridden);
            if (explicitMethodLookup.TryGetValue(overriddenKey, out var overriddenMatch))
                return overriddenMatch;

            overridden = overridden.OverriddenMethod;
        }

        return null;
    }

    /// <summary>
    /// Try to auto-include a method based on its containing type's [ActivitySource] configuration.
    /// Methods without explicit attributes can be auto-included if:
    /// - The containing type is in our types collection
    /// - The method passes the IncludeNonAsyncStateMachineMethod filter
    /// </summary>
    private static MethodMetadata? TryAutoIncludeMethod(
        IMethodSymbol invokedMethod,
        Dictionary<string, TypeMetadata> typeLookup)
    {
        if (invokedMethod.ContainingType == null) return null;

        var containingTypeFullName = GetTypeFullName(invokedMethod.ContainingType);
        if (!typeLookup.TryGetValue(containingTypeFullName, out var typeMetadata))
            return null;

        // Check if this method is already explicitly attributed (it would have been matched earlier)
        foreach (var methodInfo in typeMetadata.Methods)
        {
            if (methodInfo.MethodName != invokedMethod.Name) continue;

            // If it has explicit attributes, it was already handled
            if (methodInfo.HasActivityAttribute ||
                methodInfo.HasActivityNameAttribute ||
                methodInfo.HasNonActivityAttribute)
                return null;

            // Apply auto-include filtering
            if (!typeMetadata.IncludeNonAsyncStateMachineMethod && !methodInfo.IsAsync)
                return null;

            if (!methodInfo.IsPublic && invokedMethod.ContainingType.TypeKind != TypeKind.Interface)
                return null;

            // Build an implicit Activity MethodMetadata for this auto-included method
            return BuildImplicitActivityMethodMetadata(invokedMethod, typeMetadata);
        }

        return null;
    }

    /// <summary>
    /// Build an implicit MethodMetadata for a method that is auto-included from an [ActivitySource] type.
    /// </summary>
    private static MethodMetadata BuildImplicitActivityMethodMetadata(
        IMethodSymbol methodSymbol,
        TypeMetadata typeMetadata)
    {
        var containingTypeName = GetSimpleTypeName(methodSymbol.ContainingType);
        var activityName = $"{containingTypeName}.{methodSymbol.Name}";

        var parameters = BuildParameterInfosFromSymbol(methodSymbol);
        var returnType = BuildReturnTypeInfoFromSymbol(methodSymbol);
        var methodSymbolKey = MetadataExtractor.BuildMethodSymbolKey(methodSymbol);

        return new(
            typeMetadata.TypeFullName,
            methodSymbol.Name,
            methodSymbolKey,
            MethodMode.Activity,
            activityName,
            typeMetadata.Kind,
            typeMetadata.SuppressInstrumentation,
            AdjustStartTime: false,
            methodSymbol.IsStatic,
            methodSymbol.ReturnsVoid,
            methodSymbol.IsAsync,
            InTags: EquatableArray<TagMetadata>.Empty,
            OutTags: EquatableArray<TagMetadata>.Empty,
            parameters,
            returnType);
    }

    /// <summary>
    /// Build a ResolvedMethodInfo from an IMethodSymbol at a call site.
    /// </summary>
    private static ResolvedMethodInfo BuildResolvedMethodInfo(IMethodSymbol methodSymbol)
    {
        var containingTypeFullName = methodSymbol.ContainingType != null
            ? methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : "";

        var typeArguments = ImmutableArray.CreateBuilder<string>();
        var genericConstraints = ImmutableArray.CreateBuilder<GenericConstraintInfo>();

        // Collect containing type's type arguments — only open type parameters need to be
        // forwarded as generic type parameters on the interceptor method.  Concrete type
        // arguments (e.g. int, string) are already baked into parameter/return types and
        // must NOT appear as method-level type parameters (that would produce invalid C#
        // like `Method<int>(...)`).
        if (methodSymbol.ContainingType is { IsGenericType: true })
        {
            var originalTypeParams = methodSymbol.ContainingType.OriginalDefinition.TypeParameters;
            var typeArgs = methodSymbol.ContainingType.TypeArguments;

            for (var i = 0; i < typeArgs.Length; i++)
            {
                if (typeArgs[i].TypeKind != TypeKind.TypeParameter) continue;

                typeArguments.Add(typeArgs[i].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

                if (i < originalTypeParams.Length)
                {
                    var constraint = BuildConstraintClause(originalTypeParams[i]);
                    if (constraint != null)
                        genericConstraints.Add(new(typeArgs[i].Name, constraint));
                }
            }
        }

        // Collect method's own type arguments — same rule: only open type parameters.
        if (methodSymbol.IsGenericMethod)
        {
            var originalTypeParams = methodSymbol.OriginalDefinition.TypeParameters;
            var typeArgs = methodSymbol.TypeArguments;

            for (var i = 0; i < typeArgs.Length; i++)
            {
                if (typeArgs[i].TypeKind != TypeKind.TypeParameter) continue;

                typeArguments.Add(typeArgs[i].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

                if (i < originalTypeParams.Length)
                {
                    var constraint = BuildConstraintClause(originalTypeParams[i]);
                    if (constraint != null)
                        genericConstraints.Add(new(typeArgs[i].Name, constraint));
                }
            }
        }

        var parameters = BuildParameterInfosFromSymbol(methodSymbol);
        var returnType = BuildReturnTypeInfoFromSymbol(methodSymbol);

        return new(
            containingTypeFullName,
            methodSymbol.Name,
            methodSymbol.IsStatic,
            methodSymbol.IsExtensionMethod,
            new(typeArguments.ToImmutable()),
            new(genericConstraints.ToImmutable()),
            parameters,
            returnType);
    }

    /// <summary>
    /// Build a constraint clause string for a type parameter (e.g., "class, IDisposable, new()").
    /// Returns null if the type parameter has no constraints.
    /// </summary>
    private static string? BuildConstraintClause(ITypeParameterSymbol typeParam)
    {
        var parts = new List<string>();

        // Primary constraints (class, struct, unmanaged, notnull)
        if (typeParam.HasReferenceTypeConstraint)
            parts.Add(typeParam.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated
                ? "class?" : "class");
        else if (typeParam.HasUnmanagedTypeConstraint)
            parts.Add("unmanaged");
        else if (typeParam.HasValueTypeConstraint)
            parts.Add("struct");
        else if (typeParam.HasNotNullConstraint)
            parts.Add("notnull");

        // Type constraints (base class, interfaces)
        foreach (var constraintType in typeParam.ConstraintTypes)
            parts.Add(constraintType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        // Constructor constraint (new())
        if (typeParam.HasConstructorConstraint)
            parts.Add("new()");

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    /// <summary>
    /// Build ParameterInfo array from an IMethodSymbol.
    /// </summary>
    private static EquatableArray<ParameterInfo> BuildParameterInfosFromSymbol(IMethodSymbol methodSymbol)
    {
        var parameters = ImmutableArray.CreateBuilder<ParameterInfo>();

        foreach (var param in methodSymbol.Parameters)
        {
            var refKind = param.RefKind switch
            {
                Microsoft.CodeAnalysis.RefKind.Ref => RefKind.Ref,
                Microsoft.CodeAnalysis.RefKind.Out => RefKind.Out,
                Microsoft.CodeAnalysis.RefKind.In => RefKind.In,
                Microsoft.CodeAnalysis.RefKind.RefReadOnlyParameter => RefKind.RefReadonlyParameter,
                _ => RefKind.None
            };

            parameters.Add(new(
                param.Name,
                param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                refKind));
        }

        return new(parameters.ToImmutable());
    }

    /// <summary>
    /// Build ReturnTypeInfo from an IMethodSymbol.
    /// </summary>
    private static ReturnTypeInfo BuildReturnTypeInfoFromSymbol(IMethodSymbol methodSymbol)
    {
        var returnType = methodSymbol.ReturnType;
        var typeFullName = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var isTask = returnType.Name == "Task" &&
                     returnType.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
        var isValueTask = returnType.Name == "ValueTask" &&
                          returnType.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";

        return new(
            typeFullName,
            methodSymbol.ReturnsVoid,
            methodSymbol.IsAsync,
            isTask,
            isValueTask);
    }

    /// <summary>
    /// Get the full name of a type symbol in the same format as SyntaxExtensions.GetTypeFullName.
    /// </summary>
    private static string GetTypeFullName(INamedTypeSymbol typeSymbol)
    {
        var parts = new List<string>();
        var current = typeSymbol;

        while (current != null)
        {
            var name = current.Name;
            if (current.TypeParameters.Length > 0)
                name += "`" + current.TypeParameters.Length;

            parts.Insert(0, name);
            current = current.ContainingType;
        }

        var typePart = string.Join("+", parts);

        var ns = typeSymbol.ContainingNamespace;
        if (ns != null && !ns.IsGlobalNamespace)
            return ns.ToDisplayString() + "." + typePart;

        return typePart;
    }

    /// <summary>
    /// Get the simple type name (without namespace) for activity naming.
    /// </summary>
    private static string GetSimpleTypeName(INamedTypeSymbol typeSymbol)
    {
        var name = typeSymbol.Name;
        if (typeSymbol.TypeParameters.Length > 0)
            name += "`" + typeSymbol.TypeParameters.Length;

        return name;
    }
}
