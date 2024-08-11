using System.Reflection;

namespace OpenTelemetry.Proxy;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public class ActivitySourceAttribute : Attribute
{
    public ActivitySourceAttribute() { }

    public ActivitySourceAttribute(string activitySourceName) => ActivitySourceName = activitySourceName;

    /// <summary>Default value is type full name</summary>
    public string? ActivitySourceName { get; }

    public ActivityKind Kind { get; set; }

    /// <summary>DynamicProxy: default include all async method of interface or async return type and define [AsyncStateMachine] public or protected virtual method of class. If true, will include all method of interface and all public or protected virtual method of class.<br />StaticProxy: default include async return type and define [AsyncStateMachine] public method. If true, will include all method of class.</summary>
    public bool IncludeNonAsyncStateMachineMethod { get; set; }

    public bool SuppressInstrumentation { get; set; }

    public static string GetActivitySourceName(Type type) => GetActivitySourceName(type, null);

    internal static string GetActivitySourceName(Type type, string? activitySourceName)
    {
        var name = type.GetCustomAttribute<ActivitySourceAttribute>()?.ActivitySourceName;

        return string.IsNullOrWhiteSpace(name)
            ? string.IsNullOrWhiteSpace(activitySourceName) ? type.FullName ?? type.ToString() : activitySourceName!
            : name!;
    }
}
