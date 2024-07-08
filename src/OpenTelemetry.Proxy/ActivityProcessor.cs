namespace OpenTelemetry.Proxy;

public class ActivityProcessor : BaseProcessor<Activity>
{
    public override void OnStart(Activity data)
    {
        if (!Sdk.SuppressInstrumentation) InnerActivityAccessor.ActivityStarted(data);
    }

    public override void OnEnd(Activity data)
    {
        if (!Sdk.SuppressInstrumentation) InnerActivityAccessor.ActivityEnded(data);
    }
}
