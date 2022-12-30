using System.Reflection;

namespace OpenTelemetry.Proxy;

[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class)]
public class ActivitySourceAttribute : Attribute
{
    public ActivitySourceAttribute() { }

    public ActivitySourceAttribute(string activitySourceName) => ActivitySourceName = activitySourceName;

    /// <summary>Default value is type full name</summary>
    public string? ActivitySourceName { get; }

    public ActivityKind Kind { get; set; } = ActivityKind.Internal;

    public static string GetActivitySourceName(Type type)
    {
        var name = type.GetCustomAttribute<ActivitySourceAttribute>()?.ActivitySourceName;

        if (string.IsNullOrWhiteSpace(name)) name = type.FullName;

        return name ?? type.ToString();
    }
}
