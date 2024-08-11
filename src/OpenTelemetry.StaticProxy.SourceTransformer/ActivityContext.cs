using Microsoft.CodeAnalysis;

namespace OpenTelemetry.StaticProxy.SourceTransformer;

internal sealed class ActivityContext(string activitySourceName, string activityName)
    : IMethodTagContext
{
    public string ActivitySourceName { get; } = activitySourceName;

    public string ActivityName { get; set; } = activityName;

    public string Kind { get; set; } = "default";

    public string? ReturnValueTag { get; set; }

    public HashSet<string> UnknownTag { get; } = [];

    public Dictionary<string, ActivityTagSource> InTags { get; } = [];

    public Dictionary<string, ActivityTagSource> OutTags { get; } = [];

    public bool SuppressInstrumentation { get; set; }

    public bool IsStatic { get; set; }

    public Dictionary<SyntaxNode, LineNumber> Returns { get; } = [];
}
