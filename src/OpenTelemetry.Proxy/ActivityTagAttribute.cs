namespace OpenTelemetry.Proxy;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
public class ActivityTagAttribute : Attribute
{
    public ActivityTagAttribute() { }

    public ActivityTagAttribute(string name) => Name = string.IsNullOrWhiteSpace(name) ? null : name;

    public string? Name { get; }
}
