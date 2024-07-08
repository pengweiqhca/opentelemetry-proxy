using OpenTelemetry.Trace;

namespace OpenTelemetry.Proxy;

public static class TracerProviderBuilderExtensions
{
    public static TracerProviderBuilder AddActivityNameProcessor(this TracerProviderBuilder builder) =>
        builder.AddProcessor(new ActivityProcessor());
}
