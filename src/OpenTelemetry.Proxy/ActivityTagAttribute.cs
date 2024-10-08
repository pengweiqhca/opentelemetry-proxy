namespace OpenTelemetry.Proxy;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = true)]
public class ActivityTagAttribute : Attribute
{
    public ActivityTagAttribute() { }

    public ActivityTagAttribute(string name) => Name = string.IsNullOrWhiteSpace(name) ? null : name;

    public string? Name { get; }

    /// <summary>$.SomeProperty</summary>
    public string? Expression { get; set; }
}
