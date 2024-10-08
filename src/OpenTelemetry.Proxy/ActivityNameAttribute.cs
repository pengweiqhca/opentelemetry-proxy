﻿namespace OpenTelemetry.Proxy;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface |
    AttributeTargets.Method)]
public class ActivityNameAttribute : Attribute
{
    public ActivityNameAttribute() { }

    public ActivityNameAttribute(string activityName) =>
        ActivityName = string.IsNullOrWhiteSpace(activityName) ? null : activityName;

    /// <summary>Default value is {Type.Name}.{Method.Name}</summary>
    public string? ActivityName { get; }

    public bool AdjustStartTime { get; set; }
}
