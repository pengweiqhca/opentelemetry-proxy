namespace OpenTelemetry.StaticProxy;

internal abstract class ActivityNameContext(string activityName)
{
    public string ActivityName { get; set; } = activityName;

    public bool AdjustStartTime { get; set; }
}

internal sealed class MethodActivityNameContext(string activityName)
    : ActivityNameContext(activityName), IMethodTagContext
{
    public Dictionary<ActivityTag, string> UnknownTag { get; } = [];

    public Dictionary<ActivityTag, ActivityTagSource> InTags { get; } = [];

    public bool IsStatic { get; set; }
}

internal class TypeActivityNameContext(
    string activityName,
    MethodSyntaxContexts methods,
    Dictionary<string, MemberType> propertyOrField)
    : ActivityNameContext(activityName), ITypeContext
{
    public MethodSyntaxContexts Methods { get; } = methods;

    public Dictionary<string, MemberType> PropertyOrField { get; } = propertyOrField;

    public Dictionary<ActivityTag, string> Tags { get; } = [];

    public ITypeContext ToImplicateActivitySource(string typeFullName)
    {
        var context = new TypeActivityName2Context(typeFullName, ActivityName, Methods, PropertyOrField);

        foreach (var tag in Tags) context.Tags[tag.Key] = tag.Value;

        return context;
    }
}

internal sealed class TypeActivityName2Context(
    string activitySourceName,
    string activityName,
    MethodSyntaxContexts methods,
    Dictionary<string, MemberType> propertyOrField)
    : TypeActivityNameContext(activityName, methods, propertyOrField), IActivitySourceContext
{
    public string ActivitySourceName { get; } = activitySourceName;

    public string VariableName => "@ActivitySource@";
}
