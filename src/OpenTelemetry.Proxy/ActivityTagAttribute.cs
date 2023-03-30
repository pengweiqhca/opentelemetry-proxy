namespace OpenTelemetry.Proxy;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public class ActivityTagAttribute : Attribute
{
    public ActivityTagAttribute() { }

    public ActivityTagAttribute(string tagName) =>
        TagName = string.IsNullOrWhiteSpace(tagName) ? null : tagName;

    /// <summary>Default value is {ActivitySourceName}.{Method.Name}</summary>
    public string? TagName { get; }
}
