namespace OpenTelemetry.StaticProxy.SourceTransformer;

internal sealed class ActivitySourceContext(
    string activitySourceName,
    MethodSyntaxContexts methods,
    Dictionary<string, MemberType> propertyOrField)
    : ImplicateActivitySourceContext(activitySourceName, methods, propertyOrField)
{
    public string Kind { get; set; } = "default";

    public bool IncludeNonAsyncStateMachineMethod { get; set; }

    public bool SuppressInstrumentation { get; set; }
}

internal class ImplicateActivitySourceContext(
    string activitySourceName,
    MethodSyntaxContexts methods,
    Dictionary<string, MemberType> propertyOrField)
    : IActivitySourceContext
{
    public string ActivitySourceName { get; set; } = activitySourceName;

    public MethodSyntaxContexts Methods { get; } = methods;

    public Dictionary<string, MemberType> PropertyOrField { get; } = propertyOrField;

    public HashSet<string> Tags { get; } = [];
}
