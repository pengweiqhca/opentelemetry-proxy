using System.Reflection;

namespace OpenTelemetry.Proxy;

[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class)]
public class ActivitySourceAttribute : Attribute
{
    public ActivitySourceAttribute() { }

    public ActivitySourceAttribute(string activitySourceName) => ActivitySourceName = activitySourceName;

    /// <summary>Default value is type full name</summary>
    public string? ActivitySourceName { get; }

    public ActivityKind Kind { get; set; }

    /// <summary>Is sync method or not AsyncStateMachine</summary>
    public bool IncludeNonAsyncStateMachineMethod { get; set; }

    public static string GetActivitySourceName(Type type)
    {
        var name = type.GetCustomAttribute<ActivitySourceAttribute>()?.ActivitySourceName;

        return string.IsNullOrWhiteSpace(name) ? type.FullName ?? type.ToString() : name!;
    }
}
