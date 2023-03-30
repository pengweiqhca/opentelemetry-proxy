namespace OpenTelemetry.Proxy;

public class ActivityNameProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        if (Sdk.SuppressInstrumentation) return;

        var (name, tags) = ActivityName.GetName();

        if (name != null) data.DisplayName = name;

        if (tags == null) return;

        foreach (var kv in tags) data.SetTag(kv.Key, kv.Value);
    }
}
