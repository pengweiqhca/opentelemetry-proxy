namespace OpenTelemetry.Proxy;

public class ActivityNameProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        if (Sdk.SuppressInstrumentation) return;

        var name = ActivityName.GetName();

        if (name != null) data.DisplayName = name;
    }
}
