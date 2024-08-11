namespace OpenTelemetry.Proxy;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface |
    AttributeTargets.Method)]
public class ActivityTagsAttribute(params string[] tags) : Attribute
{
    [Obsolete("Must have at least one tag.")]
    public ActivityTagsAttribute() : this([]) { }

    /// <summary>Set activity tag, value from parameter name, property name and field name.</summary>
    public string[] Tags { get; } = tags;
}
