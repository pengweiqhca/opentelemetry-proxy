namespace OpenTelemetry.Proxy;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue)]
public class ActivityTagAttribute : Attribute
{
    public const string ReturnValueTagName = "$returnvalue";

    public ActivityTagAttribute() { }

    public ActivityTagAttribute(string name) =>
        Name = string.IsNullOrWhiteSpace(name) ? null : name;

    public string? Name { get; }
}
