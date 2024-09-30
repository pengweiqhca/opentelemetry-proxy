using System.Reflection;

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

    /// <summary>DynamicProxy: default include all async method of interface or async return type and define [AsyncStateMachine] public or protected virtual method of class. If true, will include all method of interface and all public or protected virtual method of class.<br />StaticProxy: default include async return type and define [AsyncStateMachine] public method. If true, will include all method of class.</summary>
    public bool IncludeNonAsyncStateMachineMethod { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether down stream instrumentation is suppressed (disabled).
    /// </summary>
    public bool SuppressInstrumentation { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="ActivitySource" /> variable name, it only works for static proxy. Default value is @ActivitySource@ />.
    /// </summary>
    public string? VariableName { get; set; }

    public static string GetActivitySourceName(Type type) => GetActivitySourceName(type, null);

    internal static string GetActivitySourceName(Type type, string? activitySourceName)
    {
        var name = type.GetCustomAttribute<ActivitySourceAttribute>()?.ActivitySourceName;

        return string.IsNullOrWhiteSpace(name)
            ? string.IsNullOrWhiteSpace(activitySourceName) ? type.FullName ?? type.ToString() : activitySourceName!
            : name!;
    }
}
