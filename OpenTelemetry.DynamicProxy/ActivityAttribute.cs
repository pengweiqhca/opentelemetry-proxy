namespace OpenTelemetry.DynamicProxy;

[AttributeUsage(AttributeTargets.Method)]
public class ActivityAttribute : Attribute
{
    public ActivityAttribute() { }

    public ActivityAttribute(string activityName) =>
        ActivityName = string.IsNullOrWhiteSpace(activityName) ? null : activityName;

    /// <summary>Default value is {ActivitySourceName}.{Method.Name}</summary>
    public string? ActivityName { get; }

    public ActivityKind Kind { get; set; } = ActivityKind.Internal;
}
