using System.Runtime.CompilerServices;

namespace OpenTelemetry.Proxy;

public class ActivityNameProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        if (Sdk.SuppressInstrumentation) return;

        var (name, tags) = ActivityName.GetName();

        if (name != null) data.DisplayName = name;

        SetTags(tags, data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetTags(IReadOnlyCollection<KeyValuePair<string, object?>>? tags, Activity activity)
    {
        if (tags == null || tags.Count < 1) return;

        foreach (var kv in tags) activity.SetTag(kv.Key, kv.Value);
    }
}
