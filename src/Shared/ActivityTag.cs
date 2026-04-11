#if DynamicProxy
namespace OpenTelemetry.DynamicProxy;
#else
namespace OpenTelemetry.StaticProxy;
#endif

internal sealed record ActivityTag(string TagName, string? Expression = null)
{
    public static IEnumerable<Tuple<ActivityTag, string>> Parse(IEnumerable<string> tags) =>
        from tag in tags
        let index = tag.IndexOfAny(['[', '.', '?'])
        select index < 0
            ? new Tuple<ActivityTag, string>(new(tag), tag)
            : new(new(tag, "$" + tag[index..]), tag[..index]);
}
