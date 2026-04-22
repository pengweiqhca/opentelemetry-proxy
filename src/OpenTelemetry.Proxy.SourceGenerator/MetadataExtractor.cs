using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenTelemetry.Proxy.Models;
using System.Collections.Immutable;
using RefKind = OpenTelemetry.Proxy.Models.RefKind;

namespace OpenTelemetry.Proxy;

internal static class MetadataExtractor
{

    #region ExtractTypeMetadata

    /// <summary>
    /// Extract metadata from a type annotated with [ActivitySource].
    /// Returns a TypeExtractionResult that includes any diagnostics encountered.
    /// </summary>
    public static TypeExtractionResult ExtractTypeMetadata(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var typeNode = (TypeDeclarationSyntax)context.TargetNode;
        var typeFullName = typeNode.GetTypeFullName();

        // Defaults
        var activitySourceName = typeFullName;
        var kind = "default";
        var includeNonAsync = false;
        var suppressInstrumentation = false;

        // Parse [ActivitySource] attribute arguments
        var attrData = context.Attributes.FirstOrDefault(a =>
            a.AttributeClass?.Name is "ActivitySourceAttribute" or "ActivitySource");

        if (attrData != null)
        {
            // Constructor argument: ActivitySourceName
            if (attrData.ConstructorArguments.Length > 0 &&
                attrData.ConstructorArguments[0].Value is string name &&
                !string.IsNullOrWhiteSpace(name))
                activitySourceName = name;

            // Named arguments
            foreach (var namedArg in attrData.NamedArguments)
                switch (namedArg.Key)
                {
                    case "Kind":
                        kind = ConvertKindValue(namedArg.Value, context.TargetNode, namedArg.Key, diagnostics);
                        break;
                    case "IncludeAllMethods":
                        if (!TryGetBoolValue(namedArg.Value, context.TargetNode, namedArg.Key, diagnostics, out var b1))
                            break;
                        includeNonAsync = b1;
                        break;
                    case "SuppressInstrumentation":
                        if (!TryGetBoolValue(namedArg.Value, context.TargetNode, namedArg.Key, diagnostics, out var b2))
                            break;
                        suppressInstrumentation = b2;
                        break;
                }
        }

        // Collect type-level [ActivityTags]
        var typeTags = CollectTypeLevelTags(typeNode, context.SemanticModel);

        // Collect members (fields and properties)
        var members = CollectMembers(typeNode);

        // Collect methods
        var methods = CollectMethods(typeNode);

        var metadata = new TypeMetadata(
            typeFullName,
            activitySourceName,
            kind,
            includeNonAsync,
            suppressInstrumentation,
            typeTags,
            members,
            methods);

        return new(metadata, new(diagnostics.ToImmutable()));
    }

    #endregion

    #region ExtractActivityMethodMetadata

    /// <summary>
    /// Extract metadata from a method annotated with [Activity].
    /// Returns a MethodExtractionResult that includes any diagnostics encountered.
    /// </summary>
    public static MethodExtractionResult ExtractActivityMethodMetadata(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var methodNode = (MethodDeclarationSyntax)context.TargetNode;
        var methodSymbol = (IMethodSymbol)context.TargetSymbol;
        var semanticModel = context.SemanticModel;

        var containingTypeFullName = methodNode.GetDeclaringType()?.GetTypeFullName() ?? "";
        var typeName = methodNode.GetDeclaringType()?.GetTypeName() ?? "";
        var methodName = methodNode.GetMethodName();

        // Defaults
        var activityName = $"{typeName}.{methodName}";
        var kind = "default";
        var suppressInstrumentation = false;

        // Parse [Activity] attribute
        var attrData = context.Attributes.FirstOrDefault(a =>
            a.AttributeClass?.Name is "ActivityAttribute" or "Activity");

        if (attrData != null)
        {
            if (attrData.ConstructorArguments.Length > 0 &&
                attrData.ConstructorArguments[0].Value is string name &&
                !string.IsNullOrWhiteSpace(name))
                activityName = name;

            foreach (var namedArg in attrData.NamedArguments)
                switch (namedArg.Key)
                {
                    case "Kind":
                        kind = ConvertKindValue(namedArg.Value, context.TargetNode, namedArg.Key, diagnostics);
                        break;
                    case "SuppressInstrumentation":
                        if (!TryGetBoolValue(namedArg.Value, context.TargetNode, namedArg.Key, diagnostics, out var b))
                            break;
                        suppressInstrumentation = b;
                        break;
                }
        }

        var isStatic = methodNode.IsStatic();
        var isAsync = methodNode.IsAsync();
        var isVoid = methodSymbol.IsVoid(semanticModel, methodNode.SpanStart);
        var returnType = BuildReturnTypeInfo(methodSymbol, semanticModel, methodNode.SpanStart);
        var parameters = BuildParameterInfos(methodNode);
        var methodSymbolKey = BuildMethodSymbolKey(methodSymbol);

        // Collect type-level tags from the containing type
        var typeContext = BuildTypeContextForMethod(methodNode);

        // Parse tags from [ActivityTag] on parameters and return value, and [ActivityTags] on method
        var (inTags, outTags) = CollectMethodTags(methodNode, semanticModel, typeContext, isStatic, isVoid);

        var metadata = new MethodMetadata(
            containingTypeFullName,
            methodName,
            methodSymbolKey,
            MethodMode.Activity,
            activityName,
            kind,
            suppressInstrumentation,
            AdjustStartTime: false,
            isStatic,
            isVoid,
            isAsync,
            inTags,
            outTags,
            parameters,
            returnType);

        return new(metadata, new(diagnostics.ToImmutable()));
    }

    #endregion

    #region ExtractActivityNameMetadata

    /// <summary>
    /// Extract metadata from a type or method annotated with [ActivityName].
    /// Returns an ActivityNameExtractionResult that includes any diagnostics encountered.
    /// </summary>
    public static ActivityNameExtractionResult ExtractActivityNameMetadata(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.TargetNode is TypeDeclarationSyntax typeNode)
            return ExtractActivityNameTypeMetadata(context, typeNode, ct);

        if (context.TargetNode is MethodDeclarationSyntax methodNode)
            return ExtractActivityNameMethodMetadata(context, methodNode, ct);

        // Should not happen given the predicate filter
        return new(null, EquatableArray<DiagnosticInfo>.Empty);
    }

    private static ActivityNameExtractionResult ExtractActivityNameTypeMetadata(
        GeneratorAttributeSyntaxContext context,
        TypeDeclarationSyntax typeNode,
        CancellationToken _)
    {
        var typeFullName = typeNode.GetTypeFullName();
        var typeName = typeNode.GetTypeName();

        var activityName = typeName;
        var adjustStartTime = false;

        var attrData = context.Attributes.FirstOrDefault(a =>
            a.AttributeClass?.Name is "ActivityNameAttribute" or "ActivityName");

        if (attrData != null)
        {
            if (attrData.ConstructorArguments.Length > 0 &&
                attrData.ConstructorArguments[0].Value is string name &&
                !string.IsNullOrWhiteSpace(name))
                activityName = name;

            foreach (var namedArg in attrData.NamedArguments)
                if (namedArg.Key == "AdjustStartTime" && namedArg.Value.Value is bool b)
                    adjustStartTime = b;
        }

        // Collect type-level tags
        var typeTags = CollectTypeLevelTags(typeNode, context.SemanticModel);
        var members = CollectMembers(typeNode);
        var methods = CollectMethods(typeNode);

        // For ActivityName type-level, we store activityName in ActivitySourceName field
        // and use Kind field to convey AdjustStartTime
        var metadata = new TypeMetadata(
            typeFullName,
            activityName,
            adjustStartTime ? "adjust_start_time" : "default",
            false,
            false,
            typeTags,
            members,
            methods);

        return new(metadata, EquatableArray<DiagnosticInfo>.Empty);
    }

    private static ActivityNameExtractionResult ExtractActivityNameMethodMetadata(
        GeneratorAttributeSyntaxContext context,
        MethodDeclarationSyntax methodNode,
        CancellationToken _)
    {
        var methodSymbol = (IMethodSymbol)context.TargetSymbol;
        var semanticModel = context.SemanticModel;

        var containingTypeFullName = methodNode.GetDeclaringType()?.GetTypeFullName() ?? "";
        var typeName = methodNode.GetDeclaringType()?.GetTypeName() ?? "";
        var methodName = methodNode.GetMethodName();

        var activityName = $"{typeName}.{methodName}";
        var adjustStartTime = false;

        var attrData = context.Attributes.FirstOrDefault(a =>
            a.AttributeClass?.Name is "ActivityNameAttribute" or "ActivityName");

        if (attrData != null)
        {
            if (attrData.ConstructorArguments.Length > 0 &&
                attrData.ConstructorArguments[0].Value is string name &&
                !string.IsNullOrWhiteSpace(name))
                activityName = name;

            foreach (var namedArg in attrData.NamedArguments)
                if (namedArg.Key == "AdjustStartTime" && namedArg.Value.Value is bool b)
                    adjustStartTime = b;
        }

        var isStatic = methodNode.IsStatic();
        var isAsync = methodNode.IsAsync();
        var isVoid = methodSymbol.IsVoid(semanticModel, methodNode.SpanStart);
        var returnType = BuildReturnTypeInfo(methodSymbol, semanticModel, methodNode.SpanStart);
        var parameters = BuildParameterInfos(methodNode);
        var methodSymbolKey = BuildMethodSymbolKey(methodSymbol);

        // Collect tags for ActivityName mode (only InTags, no OutTags)
        var typeContext = BuildTypeContextForMethod(methodNode);
        var (inTags, _) = CollectMethodTags(methodNode, semanticModel, typeContext, isStatic, isVoid,
            activityNameMode: true);

        var metadata = new MethodMetadata(
            containingTypeFullName,
            methodName,
            methodSymbolKey,
            MethodMode.ActivityName,
            activityName,
            Kind: null,
            SuppressInstrumentation: false,
            adjustStartTime,
            isStatic,
            isVoid,
            isAsync,
            inTags,
            OutTags: EquatableArray<TagMetadata>.Empty,
            parameters,
            returnType);

        return new(metadata, EquatableArray<DiagnosticInfo>.Empty);
    }

    #endregion

    #region ExtractNonActivityMetadata

    /// <summary>
    /// Extract metadata from a method annotated with [NonActivity].
    /// Returns a MethodExtractionResult that includes any diagnostics encountered.
    /// </summary>
    public static MethodExtractionResult ExtractNonActivityMetadata(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var methodNode = (MethodDeclarationSyntax)context.TargetNode;
        var methodSymbol = (IMethodSymbol)context.TargetSymbol;
        var semanticModel = context.SemanticModel;

        var containingTypeFullName = methodNode.GetDeclaringType()?.GetTypeFullName() ?? "";
        var methodName = methodNode.GetMethodName();

        var suppressInstrumentation = false;

        var attrData = context.Attributes.FirstOrDefault(a =>
            a.AttributeClass?.Name is "NonActivityAttribute" or "NonActivity");

        if (attrData != null)
        {
            // Constructor argument
            if (attrData.ConstructorArguments.Length > 0)
            {
                if (TryGetBoolValue(attrData.ConstructorArguments[0], context.TargetNode, "SuppressInstrumentation", diagnostics, out var b))
                    suppressInstrumentation = b;
            }

            // Named argument
            foreach (var namedArg in attrData.NamedArguments)
                if (namedArg.Key == "SuppressInstrumentation")
                {
                    if (TryGetBoolValue(namedArg.Value, context.TargetNode, namedArg.Key, diagnostics, out var b2))
                        suppressInstrumentation = b2;
                }
        }

        var isStatic = methodNode.IsStatic();
        var isAsync = methodNode.IsAsync();
        var isVoid = methodSymbol.IsVoid(semanticModel, methodNode.SpanStart);
        var returnType = BuildReturnTypeInfo(methodSymbol, semanticModel, methodNode.SpanStart);
        var parameters = BuildParameterInfos(methodNode);
        var methodSymbolKey = BuildMethodSymbolKey(methodSymbol);

        var metadata = new MethodMetadata(
            containingTypeFullName,
            methodName,
            methodSymbolKey,
            MethodMode.SuppressInstrumentation,
            ActivityName: null,
            Kind: null,
            suppressInstrumentation,
            AdjustStartTime: false,
            isStatic,
            isVoid,
            isAsync,
            InTags: EquatableArray<TagMetadata>.Empty,
            OutTags: EquatableArray<TagMetadata>.Empty,
            parameters,
            returnType);

        return new(metadata, new(diagnostics.ToImmutable()));
    }

    #endregion

    #region Tag Collection

    /// <summary>
    /// Collect type-level [ActivityTags] from all attribute lists on the type.
    /// </summary>
    private static EquatableArray<TagMetadata> CollectTypeLevelTags(
        TypeDeclarationSyntax typeNode,
        SemanticModel semanticModel)
    {
        var tags = ImmutableArray.CreateBuilder<TagMetadata>();

        var attr = typeNode.AttributeLists
            .SelectMany(al => al.Attributes)
            .FirstOrDefault(a => a.Is("ActivityTags"));

        if (attr?.ArgumentList != null)
        {
            var tagStrings = GetAttributeTagStrings(attr, semanticModel);

            foreach (var parsed in ActivityTag.Parse(tagStrings))
                tags.Add(new(
                    parsed.Item1.TagName,
                    parsed.Item2,
                    TagSource.Parameter, // Will be resolved later when used in method context
                    parsed.Item1.Expression));
        }

        return new(tags.ToImmutable());
    }

    /// <summary>
    /// Collect method-level tags: [ActivityTag] on parameters/return, [ActivityTags] on method,
    /// and type-level tags that map to parameters/fields/properties.
    /// </summary>
    private static (EquatableArray<TagMetadata> InTags, EquatableArray<TagMetadata> OutTags) CollectMethodTags(
        MethodDeclarationSyntax methodNode,
        SemanticModel semanticModel,
        SimpleTypeContext typeContext,
        bool isStatic,
        bool isVoid,
        bool activityNameMode = false)
    {
        var inTags = ImmutableArray.CreateBuilder<TagMetadata>();
        var outTags = ImmutableArray.CreateBuilder<TagMetadata>();

        // Collect unknown tags from type-level [ActivityTags] and method-level [ActivityTags]
        var unknownTags = new Dictionary<string, string>(); // tagName -> sourceName

        // Type-level tags
        if (methodNode.GetDeclaringType() is { } declaringType)
        {
            var typeAttr = declaringType.AttributeLists
                .SelectMany(al => al.Attributes)
                .FirstOrDefault(a => a.Is("ActivityTags"));

            if (typeAttr?.ArgumentList != null)
                foreach (var parsed in ActivityTag.Parse(GetAttributeTagStrings(typeAttr, semanticModel)))
                    unknownTags[parsed.Item1.TagName] = parsed.Item2;
        }

        // Method-level [ActivityTags]
        var methodAttr = methodNode.AttributeLists
            .Where(al => al.Target == null)
            .SelectMany(al => al.Attributes)
            .FirstOrDefault(a => a.Is("ActivityTags"));

        if (methodAttr?.ArgumentList != null)
            foreach (var parsed in ActivityTag.Parse(GetAttributeTagStrings(methodAttr, semanticModel)))
                unknownTags[parsed.Item1.TagName] = parsed.Item2;

        // Process return value [ActivityTag] (only for Activity mode, not ActivityName)
        if (!activityNameMode && !isVoid)
        {
            var returnTags = GetActivityTagValues("$returnvalue",
                methodNode.AttributeLists.Where(al =>
                    al.Target != null && al.Target.Identifier.ToString() == "return"),
                semanticModel);

            foreach (var tag in returnTags)
            {
                unknownTags.Remove(tag.TagName);
                outTags.Add(new(tag.TagName, "@return", TagSource.ReturnValue, tag.Expression));
            }
        }

        // Process parameters
        foreach (var parameter in methodNode.ParameterList.Parameters)
        {
            var parameterName = parameter.Identifier.ToString();
            var isRef = parameter.IsRef();
            var isOut = parameter.IsOut();

            // Get [ActivityTag] attributes on this parameter
            var paramTags = GetActivityTagValues(parameterName, parameter.AttributeLists, semanticModel);

            // Also include unknown tags that match this parameter name
            var matchingUnknownTags = unknownTags
                .Where(kv => kv.Value == parameterName)
                .Select(kv => new ActivityTag(kv.Key))
                .ToList();

            var allParamTags = paramTags.Concat(matchingUnknownTags).ToList();

            foreach (var tag in allParamTags)
            {
                if (isRef)
                {
                    // ref parameter: both InTag and OutTag
                    inTags.Add(new(tag.TagName, parameterName, TagSource.Parameter, tag.Expression));
                    if (!activityNameMode)
                        outTags.Add(new(tag.TagName, parameterName, TagSource.Parameter, tag.Expression));
                }
                else if (isOut)
                {
                    // out parameter: only OutTag
                    if (!activityNameMode)
                        outTags.Add(new(tag.TagName, parameterName, TagSource.Parameter, tag.Expression));
                }
                else
                {
                    // Normal parameter: InTag
                    inTags.Add(new(tag.TagName, parameterName, TagSource.Parameter, tag.Expression));
                }

                unknownTags.Remove(tag.TagName);
            }
        }

        // Resolve remaining unknown tags against type members (fields/properties)
        foreach (var tag in unknownTags.ToArray())
        {
            if (!typeContext.PropertyOrField.TryGetValue(tag.Value, out var memberType)) continue;

            unknownTags.Remove(tag.Key);

            if (memberType.IsStatic)
                inTags.Add(new(tag.Key, tag.Value, TagSource.StaticFieldOrProperty, null));
            else if (!isStatic)
                inTags.Add(new(tag.Key, tag.Value, TagSource.InstanceFieldOrProperty, null));
        }

        // Handle remaining unknown tags that map to $returnvalue
        if (!activityNameMode && !isVoid)
            foreach (var tag in unknownTags.Where(kv => kv.Value == "$returnvalue").ToList())
            {
                unknownTags.Remove(tag.Key);
                outTags.Add(new(tag.Key, "@return", TagSource.ReturnValue, null));
            }

        return (new(inTags.ToImmutable()),
            new(outTags.ToImmutable()));
    }

    /// <summary>
    /// Parse [ActivityTag] attributes from attribute lists and return ActivityTag records.
    /// </summary>
    private static IEnumerable<ActivityTag> GetActivityTagValues(
        string memberName,
        IEnumerable<AttributeListSyntax> attributeLists,
        SemanticModel semanticModel)
    {
        foreach (var attr in attributeLists.SelectMany(al => al.Attributes).Where(a => a.Is("ActivityTag")))
        {
            if (attr.ArgumentList == null)
            {
                yield return new(memberName);
                continue;
            }

            var name = memberName;
            string? expression = null;

            foreach (var arg in attr.ArgumentList.Arguments)
                if (arg.NameEquals == null)
                {
                    var constantValue = semanticModel.GetConstantValue(arg.Expression);
                    if (constantValue is { HasValue: true, Value: string s } && !string.IsNullOrWhiteSpace(s))
                        name = s;
                }
                else if (arg.NameEquals.Is("Expression"))
                {
                    var constantValue = semanticModel.GetConstantValue(arg.Expression);
                    if (constantValue is { HasValue: true, Value: string s })
                        expression = s;
                }

            yield return new(name, expression);
        }
    }

    /// <summary>
    /// Extract string values from [ActivityTags] attribute arguments.
    /// </summary>
    private static IEnumerable<string> GetAttributeTagStrings(
        AttributeSyntax attr,
        SemanticModel semanticModel)
    {
        if (attr.ArgumentList == null) yield break;

        foreach (var arg in attr.ArgumentList.Arguments)
        {
            var expressions = arg.Expression switch
            {
                ImplicitArrayCreationExpressionSyntax array => array.Initializer.Expressions,
                _ => Enumerable.Repeat(arg.Expression, 1)
            };

            foreach (var expression in expressions)
            {
                var constantValue = semanticModel.GetConstantValue(expression);
                if (constantValue is { HasValue: true, Value: string s })
                    yield return s;
            }
        }
    }

    #endregion

    #region Member & Method Collection

    /// <summary>
    /// Collect field and property members from a type declaration.
    /// </summary>
    private static EquatableArray<MemberInfo> CollectMembers(TypeDeclarationSyntax typeNode)
    {
        var members = ImmutableArray.CreateBuilder<MemberInfo>();

        foreach (var member in typeNode.Members)
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    var fieldIsStatic = field.IsStatic();
                    foreach (var variable in field.Declaration.Variables)
                        members.Add(new(
                            variable.Identifier.ToString(),
                            field.Declaration.Type.ToString(),
                            fieldIsStatic,
                            IsProperty: false));
                    break;

                case PropertyDeclarationSyntax property:
                    members.Add(new(
                        property.Identifier.ToString(),
                        property.Type.ToString(),
                        property.IsStatic(),
                        IsProperty: true));
                    break;
            }

        return new(members.ToImmutable());
    }

    /// <summary>
    /// Collect method info from a type declaration, applying filtering rules.
    /// </summary>
    private static EquatableArray<MethodInfo> CollectMethods(TypeDeclarationSyntax typeNode)
    {
        var methods = ImmutableArray.CreateBuilder<MethodInfo>();

        foreach (var member in typeNode.Members)
        {
            if (member is not MethodDeclarationSyntax method) continue;

            var hasActivity = method.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => a.Is("Activity"));

            var hasActivityName = method.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => a.Is("ActivityName"));

            var hasNonActivity = method.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => a.Is("NonActivity"));

            var isAsync = method.IsAsync();
            var isPublic = method.IsPublic();

            methods.Add(new(
                method.GetMethodName(),
                hasActivity,
                hasActivityName,
                hasNonActivity,
                isAsync,
                isPublic));
        }

        return new(methods.ToImmutable());
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Build a unique string key for a method symbol.
    /// Format: ContainingType.MethodName(ParamType1, ParamType2, ...)
    /// </summary>
    internal static string BuildMethodSymbolKey(IMethodSymbol methodSymbol)
    {
        var containingType = methodSymbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "";
        var methodName = methodSymbol.Name;
        var parameters = string.Join(", ",
            methodSymbol.Parameters.Select(p =>
                p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

        return $"{containingType}.{methodName}({parameters})";
    }

    /// <summary>
    /// Build ReturnTypeInfo from a method symbol.
    /// </summary>
    private static ReturnTypeInfo BuildReturnTypeInfo(
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        int position)
    {
        var returnType = methodSymbol.ReturnType;
        var typeFullName = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var isVoid = methodSymbol.IsVoid(semanticModel, position);
        var isAsync = methodSymbol.IsAsync;

        var isTask = returnType.Name == "Task" &&
                     returnType.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
        var isValueTask = returnType.Name == "ValueTask" &&
                          returnType.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";

        return new(typeFullName, isVoid, isAsync, isTask, isValueTask);
    }

    /// <summary>
    /// Build parameter infos from a method declaration syntax.
    /// </summary>
    private static EquatableArray<ParameterInfo> BuildParameterInfos(MethodDeclarationSyntax methodNode)
    {
        var parameters = ImmutableArray.CreateBuilder<ParameterInfo>();

        foreach (var param in methodNode.ParameterList.Parameters)
        {
            var name = param.Identifier.ToString();
            var typeName = param.Type?.ToString() ?? "object";
            var refKind = param.IsRef() ? RefKind.Ref
                : param.IsOut() ? RefKind.Out
                : param.Modifiers.Any(m => m.ToString() == "in") ? RefKind.In
                : RefKind.None;

            parameters.Add(new(name, typeName, refKind));
        }

        return new(parameters.ToImmutable());
    }

    /// <summary>
    /// Convert an ActivityKind TypedConstant to a string representation for code generation.
    /// Reports diagnostics when the value is unrecognized, null, or wrong type.
    /// </summary>
    private static string ConvertKindValue(
        TypedConstant typedConstant,
        SyntaxNode targetNode,
        string _,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (typedConstant.Kind == TypedConstantKind.Error)
        {
            diagnostics.Add(CreateDiagnosticInfo(
                DiagnosticDescriptors.UnrecognizedAttributeArg,
                targetNode,
                typedConstant.ToString()));
            return "default";
        }

        if (typedConstant.Value == null)
        {
            diagnostics.Add(CreateDiagnosticInfo(
                DiagnosticDescriptors.InvalidAttributeArgValue,
                targetNode));
            return "default";
        }

        if (typedConstant.Value is int kind)
            return "(System.Diagnostics.ActivityKind)" + kind;

        diagnostics.Add(CreateDiagnosticInfo(
            DiagnosticDescriptors.InvalidAttributeArgType,
            targetNode,
            "ActivityKind", typedConstant.Value.GetType().Name));
        return "default";
    }

    /// <summary>
    /// Try to get a bool value from a TypedConstant, reporting diagnostics on failure.
    /// </summary>
    private static bool TryGetBoolValue(
        TypedConstant typedConstant,
        SyntaxNode targetNode,
        string _,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out bool value)
    {
        value = false;

        if (typedConstant.Kind == TypedConstantKind.Error)
        {
            diagnostics.Add(CreateDiagnosticInfo(
                DiagnosticDescriptors.UnrecognizedAttributeArg,
                targetNode,
                typedConstant.ToString()));
            return false;
        }

        if (typedConstant.Value == null)
        {
            diagnostics.Add(CreateDiagnosticInfo(
                DiagnosticDescriptors.InvalidAttributeArgValue,
                targetNode));
            return false;
        }

        if (typedConstant.Value is bool b)
        {
            value = b;
            return true;
        }

        diagnostics.Add(CreateDiagnosticInfo(
            DiagnosticDescriptors.InvalidAttributeArgType,
            targetNode,
            "Boolean", typedConstant.Value.GetType().Name));
        return false;
    }

    /// <summary>
    /// Create a DiagnosticInfo from a descriptor and syntax node location.
    /// </summary>
    internal static DiagnosticInfo CreateDiagnosticInfo(
        DiagnosticDescriptor descriptor,
        SyntaxNode node,
        params string[] messageArgs)
    {
        var location = node.GetLocation();
        var lineSpan = location.GetLineSpan();
        var locationInfo = new LocationInfo(
            lineSpan.Path,
            lineSpan.StartLinePosition.Line,
            lineSpan.StartLinePosition.Character,
            lineSpan.EndLinePosition.Line,
            lineSpan.EndLinePosition.Character);

        return new(
            descriptor,
            locationInfo,
            new(ImmutableArray.Create(messageArgs)));
    }

    /// <summary>
    /// Build a simple type context for resolving tags against type members.
    /// </summary>
    private static SimpleTypeContext BuildTypeContextForMethod(
        MethodDeclarationSyntax methodNode)
    {
        var propertyOrField = new Dictionary<string, MemberType>();

        if (methodNode.GetDeclaringType() is { } typeNode)
            foreach (var member in typeNode.Members)
                switch (member)
                {
                    case FieldDeclarationSyntax field:
                        var fieldIsStatic = field.IsStatic();
                        foreach (var variable in field.Declaration.Variables)
                            propertyOrField[variable.Identifier.ToString()] =
                                new(fieldIsStatic, false);
                        break;

                    case PropertyDeclarationSyntax property:
                        propertyOrField[property.Identifier.ToString()] =
                            new(property.IsStatic(), true);
                        break;
                }

        return new(propertyOrField);
    }

    #endregion
}

/// <summary>
/// Simple type context for tag resolution during metadata extraction.
/// </summary>
internal sealed class SimpleTypeContext(Dictionary<string, MemberType> propertyOrField)
{
    public Dictionary<string, MemberType> PropertyOrField { get; } = propertyOrField;
}
