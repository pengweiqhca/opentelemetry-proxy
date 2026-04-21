using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OpenTelemetry.Proxy.Demo;

record ConfigureOtlp(IServiceProvider Services)
{
    public static MeterProviderBuilder ConfigureResource(
        MeterProviderBuilder tracerProviderBuilder)
    {
        if (tracerProviderBuilder is IDeferredMeterProviderBuilder meterProviderBuilder1)
            meterProviderBuilder1.Configure((provider, mpb) =>
                mpb.ConfigureResource(new ConfigureOtlp(provider).ConfigureResource));

        return tracerProviderBuilder;
    }

    public static TracerProviderBuilder ConfigureResource(
        TracerProviderBuilder tracerProviderBuilder)
    {
        if (tracerProviderBuilder is IDeferredTracerProviderBuilder tracerProviderBuilder1)
            tracerProviderBuilder1.Configure((provider, mpb) =>
                mpb.ConfigureResource(new ConfigureOtlp(provider).ConfigureResource));

        return tracerProviderBuilder;
    }

    private void ConfigureResource(ResourceBuilder builder) =>
        builder.AddService(Services.GetRequiredService<IHostEnvironment>().ApplicationName);
}
