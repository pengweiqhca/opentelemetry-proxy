namespace OpenTelemetry.Proxy;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface |
    AttributeTargets.Method)]
public class ActivityTagsAttribute(params string[] tags) : Attribute
{
    [Obsolete("Must have at least one tag.", true)]
    public ActivityTagsAttribute() : this([]) { }

    /// <summary>Set activity tag, value from parameter name, property name and field name, support expression.</summary>
    public string[] Tags { get; } = tags;
}
