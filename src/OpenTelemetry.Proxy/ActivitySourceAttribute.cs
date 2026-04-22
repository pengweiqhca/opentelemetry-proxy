namespace OpenTelemetry.Proxy;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public class ActivitySourceAttribute : Attribute
{
    public ActivitySourceAttribute() { }

    public ActivitySourceAttribute(string activitySourceName) => ActivitySourceName = activitySourceName;

    /// <summary>Default value is type full name</summary>
    public string? ActivitySourceName { get; }

    /// <summary>
    /// Gets or sets the relationship between the Activity, its parents, and its children in a Trace.
    /// </summary>
    public ActivityKind Kind { get; set; }

    /// <summary>By default, only async methods (or methods returning Task/ValueTask for interfaces) are auto-included. Set to true to include all public methods.</summary>
    public bool IncludeAllMethods { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether down stream instrumentation is suppressed (disabled).
    /// </summary>
    public bool SuppressInstrumentation { get; set; }
}
