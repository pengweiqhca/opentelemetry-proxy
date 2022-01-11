namespace OpenTelemetry.DynamicProxy;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Class)]
public class ActivityNameAttribute : Attribute
{
    public ActivityNameAttribute() { }

    public ActivityNameAttribute(string activityName) =>
        ActivityName = string.IsNullOrWhiteSpace(activityName) ? null : activityName;

    /// <summary>Default value is {Type.FullName}.{Method.Name}</summary>
    public string? ActivityName { get; }

    /// <summary>&lt;0: unlimited, default value is 1.</summary>
    public int MaxUseableTimes { get; set; } = 1;
}
