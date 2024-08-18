namespace OpenTelemetry.StaticProxy.SourceTransformer;

internal sealed class ActivityContext(string activitySourceName, string activityName)
    : IMethodTagContext
{
    public string ActivitySourceName { get; } = activitySourceName;

    public string ActivityName { get; set; } = activityName;

    public string Kind { get; set; } = "default";

    public HashSet<ActivityTag> ReturnValueTag { get; } = [];

    public HashSet<ActivityTag> UnknownTag { get; } = [];

    public Dictionary<ActivityTag, ActivityTagSource> InTags { get; } = [];

    public Dictionary<ActivityTag, ActivityTagSource> OutTags { get; } = [];

    public bool SuppressInstrumentation { get; set; }

    public bool IsStatic { get; set; }
}
