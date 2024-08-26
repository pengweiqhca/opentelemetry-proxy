using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics.CodeAnalysis;

namespace OpenTelemetry.StaticProxy;

internal sealed class ProxyVisitor(
    Compilation compilation,
    TypeSyntaxContexts types,
    Dictionary<string, TypeMethods> typeContexts,
    Action<Diagnostic> reportDiagnostic)
    : CSharpSyntaxRewriter
{
    #region VisitTypeDeclaration

    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        VisitTypeDeclaration(node);

        return base.VisitClassDeclaration(node);
    }

    public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
    {
        VisitTypeDeclaration(node);

        return base.VisitStructDeclaration(node);
    }

    public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        VisitTypeDeclaration(node);

        return base.VisitRecordDeclaration(node);
    }

    public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        VisitTypeDeclaration(node);

        return base.VisitInterfaceDeclaration(node);
    }

    private void VisitTypeDeclaration(TypeDeclarationSyntax node)
    {
        if (!types.FullNames.TryGetValue(node, out var typeFullName) ||
            !types.Types.TryGetValue(typeFullName, out var typeSyntaxContext) ||
            typeContexts.ContainsKey(typeFullName)) return;

        var attributes = typeSyntaxContext.Types.SelectMany(type => type.AttributeLists).ToArray();

        AttributeSyntax? attribute = null;

        ITypeContext? typeContext = null;
        foreach (var attr in attributes.SelectMany(attr => attr.Attributes))
        {
            if (attr.Is("ActivityName"))
            {
                attribute = attr;

                continue;
            }

            if (!attr.Is("ActivitySource")) continue;

            ActivitySourceContext context = new(typeFullName, typeSyntaxContext.Methods,
                typeSyntaxContext.PropertyOrField);

            typeContext = context;

            if (attr.ArgumentList != null)
                foreach (var arg in attr.ArgumentList.Arguments)
                    if (arg.NameEquals == null)
                    {
                        if (TryGetRequiredValue(arg, out var value)) context.ActivitySourceName = value;
                    }
                    else if (arg.NameEquals.Name.ToString().Equals("Kind", StringComparison.Ordinal))
                        context.Kind = GetKindValue(arg);
                    else if (arg.NameEquals.Name.ToString()
                             .Equals("IncludeNonAsyncStateMachineMethod", StringComparison.Ordinal))
                        context.IncludeNonAsyncStateMachineMethod = GetValue<bool>(arg);
                    else if (arg.NameEquals.Name.ToString()
                             .Equals("SuppressInstrumentation", StringComparison.Ordinal))
                        context.SuppressInstrumentation = GetValue<bool>(arg);

            break;
        }

        var typeName = node.GetTypeName();

        if (typeContext == null && attribute != null)
            typeContext = ParseActivityName(attribute, typeName, typeSyntaxContext);

        typeContexts[typeFullName] = new(typeContext ??=
                new NoAttributeTypeContext(typeSyntaxContext.Methods, typeSyntaxContext.PropertyOrField),
            typeName, typeFullName, node);

        foreach (var activityTag in GetActivityTags(attributes))
            typeContext.Tags[activityTag.Item1] = activityTag.Item2;
    }

    #endregion

    #region VisitMethodDeclaration

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (node.GetDeclaringType() is { } typeSyntax &&
            types.FullNames.TryGetValue(typeSyntax, out var fullName) &&
            typeContexts.TryGetValue(fullName, out var typeContext))
            GetProxyMethodContext(typeContext, node);

        return base.VisitMethodDeclaration(node);
    }

    private void GetProxyMethodContext(TypeMethods typeMethods, MethodDeclarationSyntax method)
    {
        AttributeSyntax? attr1 = null;
        AttributeSyntax? attr2 = null;

        if (!typeMethods.Context.Methods.TryGetValue(method, out var def)) return;

        var attributes = method.AttributeLists.Union(def?.AttributeLists ?? []).ToArray();

        foreach (var attr in attributes.SelectMany(attr => attr.Attributes))
        {
            if (attr.Is("NonActivity"))
            {
                if (attr.ArgumentList is { Arguments: [var arg, ..] } && GetValue<bool>(arg))
                    typeMethods.AddMethod(method, new SuppressInstrumentationContext());

                return;
            }

            if (attr.Is("Activity")) attr1 = attr;
            else if (attr.Is("ActivityName")) attr2 = attr;
        }

        if (typeMethods.TypeNode is not InterfaceDeclarationSyntax && !method.IsPublic()) return;

        IMethodTagContext? context;

        if (attr1 != null)
        {
            context = ProcessActivity(typeMethods, method, attr1);

            if (typeMethods.Context is NoAttributeTypeContext noAttributeTypeContext)
                typeMethods.Context = noAttributeTypeContext.ToImplicateActivitySource(typeMethods.TypeFullName);
        }
        else if (attr2 == null || (context = ParseActivityName(attr2, method,
                     typeMethods.Context is ActivityNameContext anc ? anc.ActivityName : typeMethods.TypeName)) == null)
            context = typeMethods.Context switch
            {
                ActivityNameContext typeContext => new MethodActivityNameContext(
                    string.IsNullOrWhiteSpace(typeContext.ActivityName)
                        ? GetActivityName(method, null, typeMethods.TypeName)
                        : $"{typeContext.ActivityName}.{method.GetMethodName()}")
                {
                    MaxUsableTimes = typeContext.MaxUsableTimes
                },
                ActivitySourceContext typeContext
                    when typeContext.IncludeNonAsyncStateMachineMethod || method.IsAsync() =>
                    new ActivityContext(typeContext.ActivitySourceName,
                        GetActivityName(method, null, typeMethods.TypeName))
                    {
                        Kind = typeContext.Kind,
                        SuppressInstrumentation = typeContext.SuppressInstrumentation,
                    },
                _ => null
            };

        if (context == null) return;

        context.IsStatic = method.IsStatic();

        foreach (var tag in typeMethods.Context.Tags) context.UnknownTag[tag.Key] = tag.Value;
        foreach (var tag in GetActivityTags(attributes)) context.UnknownTag[tag.Item1] = tag.Item2;

        ProcessActivityTags(method, typeMethods.Context, context);

        typeMethods.AddMethod(method, context);
    }

    private ActivityContext ProcessActivity(TypeMethods typeMethods, MethodDeclarationSyntax method,
        AttributeSyntax attribute)
    {
        var context = new ActivityContext(typeMethods.Context is ActivitySourceContext tc
            ? tc.ActivitySourceName
            : typeMethods.TypeFullName, GetActivityName(method, null, typeMethods.TypeName));

        if (attribute.ArgumentList == null)
            context.ActivityName = GetActivityName(method, null, typeMethods.TypeName);
        else
            foreach (var arg in attribute.ArgumentList.Arguments)
                if (arg.NameEquals == null)
                {
                    if (TryGetRequiredValue(arg, out var value)) context.ActivityName = value;
                }
                else if (arg.NameEquals.Name.ToString().Equals("Kind", StringComparison.Ordinal))
                    context.Kind = GetKindValue(arg);
                else if (arg.NameEquals.Name.ToString().Equals("SuppressInstrumentation", StringComparison.Ordinal))
                    context.SuppressInstrumentation = GetValue<bool>(arg);

        return context;
    }

    #endregion

    #region ActivityName

    private TypeActivityNameContext? ParseActivityName(AttributeSyntax attribute, string typeName,
        TypeSyntaxContext typeContext)
    {
        var context = new TypeActivityNameContext(typeName, typeContext.Methods, typeContext.PropertyOrField);

        if (attribute.ArgumentList == null) return context;

        foreach (var arg in attribute.ArgumentList.Arguments)
            if (arg.NameEquals == null)
            {
                if (TryGetRequiredValue(arg, out var value)) context.ActivityName = value;
            }
            else if (arg.NameEquals.Name.ToString().Equals("MaxUsableTimes", StringComparison.Ordinal))
                context.MaxUsableTimes = GetValue<int>(arg);

        if (string.IsNullOrWhiteSpace(context.ActivityName)) context.ActivityName = typeName;

        return context.MaxUsableTimes == 0 ? null : context;
    }

    private MethodActivityNameContext? ParseActivityName(AttributeSyntax attribute, MethodDeclarationSyntax method,
        string parentName)
    {
        var context = new MethodActivityNameContext(GetActivityName(method, null, parentName));

        if (attribute.ArgumentList == null) return context;

        foreach (var arg in attribute.ArgumentList.Arguments)
            if (arg.NameEquals == null)
            {
                if (TryGetRequiredValue(arg, out var value)) context.ActivityName = value;
            }
            else if (arg.NameEquals.Name.ToString().Equals("MaxUsableTimes", StringComparison.Ordinal))
                context.MaxUsableTimes = GetValue<int>(arg);

        return context.MaxUsableTimes == 0 ? null : context;
    }

    private static string GetActivityName(MethodDeclarationSyntax method, string? activityName, string typeName) =>
        string.IsNullOrWhiteSpace(activityName) ? $"{typeName}.{method.GetMethodName()}" : activityName!;

    #endregion

    #region ActivityTag

    private IEnumerable<Tuple<ActivityTag, string>> GetActivityTags(IEnumerable<AttributeListSyntax> attributes)
    {
        var attr = attributes.SelectMany(x => x.Attributes).FirstOrDefault(attr => attr.Is("ActivityTags"));

        return attr?.ArgumentList == null
            ? []
            : ActivityTag.Parse(attr.ArgumentList.Arguments.SelectMany(GetValues<string>));
    }

    private void ProcessActivityTags(MethodDeclarationSyntax method, ITypeContext typeContext,
        IMethodTagContext methodContext)
    {
        var semanticModel = compilation.GetSemanticModel(method.SyntaxTree);
        var isVoid = semanticModel.GetDeclaredSymbol(method) is not { } methodSymbol ||
            methodSymbol.IsVoid(semanticModel, method.SpanStart);

        var activityContext = methodContext as ActivityContext;
        if (activityContext != null && !isVoid)
            foreach (var tag in GetActivityTagValue("$returnvalue", method.AttributeLists.Where(attributeList =>
                         attributeList.Target != null && attributeList.Target.Identifier.ToString() == "return")))
            {
                methodContext.UnknownTag.Remove(tag);
                activityContext.OutTags[tag] = ActivityTagSource.ReturnValue;
            }

        foreach (var parameter in method.ParameterList.Parameters)
        {
            var parameterName = parameter.Identifier.ToString();
            var source = new ActivityTagSource(parameterName, ActivityTagFrom.ArgumentOrLocalVariable);

            foreach (var tag in GetActivityTagValue(parameterName, parameter.AttributeLists).Union(methodContext
                         .UnknownTag.Where(kv => kv.Value == parameterName).Select(kv => kv.Key)).ToList())
            {
                if (parameter.IsRef())
                {
                    if (activityContext != null) activityContext.OutTags[tag] = source;

                    methodContext.InTags[tag] = source;
                }
                else if (!parameter.IsOut()) methodContext.InTags[tag] = source;
                else if (activityContext != null) activityContext.OutTags[tag] = source;

                methodContext.UnknownTag.Remove(tag);
            }
        }

        foreach (var tag in methodContext.UnknownTag.ToArray())
        {
            if (!typeContext.PropertyOrField.TryGetValue(tag.Value, out var type)) continue;

            methodContext.UnknownTag.Remove(tag.Key);

            if (type.IsStatic)
                methodContext.InTags[tag.Key] = new(tag.Value, ActivityTagFrom.StaticFieldOrProperty);
            else if (!methodContext.IsStatic)
                methodContext.InTags[tag.Key] = new(tag.Value, ActivityTagFrom.InstanceFieldOrProperty);
        }

        if (activityContext == null || isVoid) return;

        foreach (var tag in methodContext.UnknownTag.Where(kv => kv.Value == "$returnvalue").Select(kv => kv.Key)
                     .ToList())
        {
            methodContext.UnknownTag.Remove(tag);
            activityContext.OutTags[tag] = ActivityTagSource.ReturnValue;
        }
    }

    private IEnumerable<ActivityTag> GetActivityTagValue(string memberName, IEnumerable<AttributeListSyntax> attributes)
    {
        foreach (var attr in attributes.SelectMany(attr => attr.Attributes)
                     .Where(static attr => attr.Is("ActivityTag")))
            if (attr.ArgumentList == null) yield return new(memberName);
            else
            {
                var name = memberName;

                string? expression = null;
                foreach (var arg in attr.ArgumentList.Arguments)
                    if (arg.NameEquals == null)
                    {
                        if (TryGetRequiredValue(arg, out var value) && !string.IsNullOrWhiteSpace(value))
                            name = value;
                    }
                    else if (arg.NameEquals.Name.ToString().Equals("Expression", StringComparison.Ordinal))
                        expression = GetValue<string>(arg);

                yield return new(name, expression);
            }
    }

    #endregion

    #region AttributeArgument

    private bool TryGetRequiredValue(AttributeArgumentSyntax arg,
        [NotNullWhen(true)] out string? value) =>
        TryGetRequiredValue(compilation.GetSemanticModel(arg.SyntaxTree), arg.Expression, out value) &&
        !string.IsNullOrWhiteSpace(value);

    private T? GetValue<T>(AttributeArgumentSyntax arg) =>
        TryGetRequiredValue<T>(compilation.GetSemanticModel(arg.SyntaxTree), arg.Expression, out var value)
            ? value
            : default;

    private string GetKindValue(AttributeArgumentSyntax arg)
    {
        var constantValue = compilation.GetSemanticModel(arg.SyntaxTree).GetConstantValue(arg.Expression);
        if (constantValue.HasValue)
        {
            if (constantValue.Value == null)
                reportDiagnostic(Diagnostic.Create(InvalidAttributeArgValue, arg.Expression.GetLocation()));
            else if (constantValue.Value is int kind) return "(System.Diagnostics.ActivityKind)" + kind;
            else
                reportDiagnostic(Diagnostic.Create(InvalidAttributeArgType, arg.Expression.GetLocation(),
                    "ActivityKind", constantValue.Value.GetType().Name));

            return "default";
        }

        var str = arg.Expression.ToString();

        if (str == "default" || str.StartsWith("default(", StringComparison.Ordinal) ||
            str.Split('.') is [.., "ActivityKind", _]) return str;

        reportDiagnostic(Diagnostic.Create(UnrecognizedAttributeArg, arg.Expression.GetLocation(),
            str));

        return "default";
    }

    private IEnumerable<T> GetValues<T>(AttributeArgumentSyntax arg)
    {
        var expressions = arg.Expression switch
        {
            ImplicitArrayCreationExpressionSyntax array => array.Initializer.Expressions,
#if CollectionExpression
            CollectionExpressionSyntax collection => collection.Elements.OfType<ExpressionElementSyntax>()
                .Select(x => x.Expression),
#endif
            _ => Enumerable.Repeat(arg.Expression, 1)
        };

        var semanticModel = compilation.GetSemanticModel(arg.SyntaxTree);

        foreach (var expression in expressions)
            if (TryGetRequiredValue<T>(semanticModel, expression, out var value))
                yield return value;
    }

#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor UnrecognizedAttributeArg = new(
        "OTSP001",
        "Unrecognized attribute argument",
        "Unrecognized attribute argument expression '{0}'",
        "OpenTelemetry.StaticProxy",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor InvalidAttributeArgValue = new(
        "OTSP002",
        "Invalid attribute argument value",
        "Excepted attribute argument is not null, or remove this argument value",
        "OpenTelemetry.StaticProxy",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor InvalidAttributeArgType = new(
        "OTSP003",
        "Invalid attribute argument type",
        "Excepted attribute argument type is '{0}' but found '{1}'",
        "OpenTelemetry.StaticProxy",
        DiagnosticSeverity.Error,
        true);

    private bool TryGetValue(SemanticModel semantic, ExpressionSyntax expression, out object? value)
    {
        var constantValue = semantic.GetConstantValue(expression);
        if (constantValue.HasValue)
        {
            value = constantValue.Value;
            return true;
        }

        reportDiagnostic(Diagnostic.Create(UnrecognizedAttributeArg, expression.GetLocation(),
            expression.ToString()));

        value = null;
        return false;
    }

    private bool TryGetRequiredValue<T>(SemanticModel semantic, ExpressionSyntax expression,
        [NotNullWhen(true)] out T? value)
    {
        if (TryGetValue(semantic, expression, out var v))
        {
            if (v is T t)
            {
                value = t;

                return true;
            }

            reportDiagnostic(v is null
                ? Diagnostic.Create(InvalidAttributeArgValue, expression.GetLocation())
                : Diagnostic.Create(InvalidAttributeArgType, expression.GetLocation(),
                    typeof(T).Name, v.GetType().Name));
        }

        value = default;
        return false;
    }

    #endregion

    public static IReadOnlyCollection<TypeMethods> GetProxyMethods(Compilation compilation,
        Action<Diagnostic> reportDiagnostic)
    {
        var partialVisitor = new PartialVisitor(compilation);

        foreach (var tree in compilation.SyntaxTrees)
            partialVisitor.Visit(tree.GetRoot());

        Dictionary<string, TypeMethods> typeContexts = [];

        var proxyVisitor = new ProxyVisitor(compilation, partialVisitor.Types, typeContexts, reportDiagnostic);

        foreach (var tree in compilation.SyntaxTrees)
            proxyVisitor.Visit(tree.GetRoot());

        return typeContexts.Values;
    }
}
