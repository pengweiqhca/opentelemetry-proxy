namespace OpenTelemetry.Proxy;

[AttributeUsage(AttributeTargets.Method)]
public class ActivityAttribute : Attribute
{
    public ActivityAttribute() { }

    public ActivityAttribute(string activityName) => ActivityName = string.IsNullOrWhiteSpace(activityName) ? null : activityName;

    /// <summary>Default value is {ActivitySourceName}.{Method.Name}</summary>
    public string? ActivityName { get; }

    public ActivityKind Kind { get; set; }

    /// <summary>Set activity tag, value from parameter name, property name and field name.</summary>
    public string[]? Tags { get; set; }

    public bool SuppressInstrumentation { get; set; }
}
