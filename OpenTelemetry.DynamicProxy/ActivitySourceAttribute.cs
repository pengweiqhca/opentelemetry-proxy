namespace OpenTelemetry.DynamicProxy;

[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class)]
public class ActivitySourceAttribute : Attribute
{
    public ActivitySourceAttribute() { }

    public ActivitySourceAttribute(string activitySourceName) => ActivitySourceName = activitySourceName;

    /// <summary>Default value is type full name</summary>
    public string? ActivitySourceName { get; }

    public static string GetActivitySourceName(Type type)
    {
        var name = type.GetCustomAttribute<ActivitySourceAttribute>()?.ActivitySourceName;

        if (string.IsNullOrWhiteSpace(name)) name = type.FullName;

        return name ?? type.ToString();
    }
}
