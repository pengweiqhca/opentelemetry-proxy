﻿#if DynamicProxy
namespace OpenTelemetry.DynamicProxy;
#else
namespace OpenTelemetry.StaticProxy;
#endif

internal sealed record ActivityTag(string TagName, string? Expression = null)
{
    public static IEnumerable<Tuple<ActivityTag, string>> Parse(IEnumerable<string> tags)
    {
        foreach (var tag in tags)
        {
            var index = tag.IndexOfAny(['[', '.', '?']);

            yield return index < 0 ? new(new(tag), tag) : new(new(tag, "$" + tag[index..]), tag[..index]);
        }
    }
}
