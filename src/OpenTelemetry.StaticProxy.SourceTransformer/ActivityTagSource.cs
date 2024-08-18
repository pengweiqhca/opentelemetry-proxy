namespace OpenTelemetry.StaticProxy.SourceTransformer;

internal record struct ActivityTagSource(string Name, ActivityTagFrom From);

public record ActivityTag(string Name, string? Expression = null)
{
    public static ActivityTag Parse(string tag)
    {
        var index = tag.IndexOfAny(['[', '.']);

        return index < 0 ? new(tag, null) : new(tag[..index], "$" + tag[(index + 1)..]);
    }

    public static IEnumerable<ActivityTag> Parse(IEnumerable<string> tags) => tags.Select(Parse);

    public static implicit operator ActivityTag(string tag) => Parse(tag);
}

internal enum ActivityTagFrom
{
    Argument,
    InstanceFieldOrProperty,
    StaticFieldOrProperty,
}
