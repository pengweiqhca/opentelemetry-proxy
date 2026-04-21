namespace OpenTelemetry.Proxy;

[AttributeUsage(AttributeTargets.Method)]
public class ActivityAttribute : Attribute
{
    public ActivityAttribute() { }

    public ActivityAttribute(string activityName) => ActivityName = string.IsNullOrWhiteSpace(activityName) ? null : activityName;

    /// <summary>Default value is {ActivitySourceName}.{Method.Name}</summary>
    public string? ActivityName { get; }

    /// <summary>
    /// Gets or sets the relationship between the Activity, its parents, and its children in a Trace.
    /// </summary>
    public ActivityKind Kind { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether down stream instrumentation is suppressed (disabled).
    /// </summary>
    public bool SuppressInstrumentation { get; set; }
}
