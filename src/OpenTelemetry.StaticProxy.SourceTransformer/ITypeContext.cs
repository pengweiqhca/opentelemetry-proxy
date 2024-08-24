namespace OpenTelemetry.StaticProxy;

internal interface ITypeContext
{
    MethodSyntaxContexts Methods { get; }

    Dictionary<string, MemberType> PropertyOrField { get; }

    HashSet<string> Tags { get; }
}

internal record struct MemberType(bool IsStatic, bool IsProperty);

internal interface IActivitySourceContext : ITypeContext
{
    string ActivitySourceName { get; }
}
