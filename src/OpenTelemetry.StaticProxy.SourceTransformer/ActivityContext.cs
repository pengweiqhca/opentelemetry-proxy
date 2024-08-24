namespace OpenTelemetry.StaticProxy;

internal sealed class ActivityContext(string activitySourceName, string activityName)
    : IMethodTagContext
{
    public string ActivitySourceName { get; } = activitySourceName;

    public string ActivityName { get; set; } = activityName;

    public string Kind { get; set; } = "default";

    public Dictionary<ActivityTag, string> UnknownTag { get; } = [];

    public Dictionary<ActivityTag, ActivityTagSource> InTags { get; } = [];

    public Dictionary<ActivityTag, ActivityTagSource> OutTags { get; } = [];

    public bool SuppressInstrumentation { get; set; }

    public bool IsStatic { get; set; }
}
