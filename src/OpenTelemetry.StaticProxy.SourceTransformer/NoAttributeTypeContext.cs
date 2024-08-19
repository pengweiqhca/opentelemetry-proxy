namespace OpenTelemetry.StaticProxy.SourceTransformer;

internal sealed class NoAttributeTypeContext(
    MethodSyntaxContexts methods,
    Dictionary<string, MemberType> propertyOrField)
    : ITypeContext
{
    public MethodSyntaxContexts Methods { get; } = methods;

    public Dictionary<string, MemberType> PropertyOrField { get; } = propertyOrField;

    public Dictionary<ActivityTag, string> Tags { get; } = [];

    public ITypeContext ToImplicateActivitySource(string typeFullName)
    {
        var context = new ImplicateActivitySourceContext(typeFullName, Methods, PropertyOrField);

        foreach (var tag in Tags) context.Tags[tag.Key] = tag.Value;

        return context;
    }
}
