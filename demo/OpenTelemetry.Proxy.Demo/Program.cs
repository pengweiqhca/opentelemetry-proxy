using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var webApplicationBuilder = WebApplication.CreateBuilder(args);

webApplicationBuilder.Services.AddDemoClass().AddLogging(lb => lb.AddOpenTelemetry()).AddOpenTelemetry()
    .WithMetrics(builder => builder.ConfigureBuilder((provider, mpb) => mpb
            .ConfigureResource(new ConfigureOtlp(provider).ConfigureResource))
        .AddOtlpExporter())
    .WithTracing(builder => builder.ConfigureBuilder((provider, mpb) => mpb
            .ConfigureResource(new ConfigureOtlp(provider).ConfigureResource))
        .AddAspNetCoreInstrumentation()
        .AddSource("DemoClass")
        .AddOtlpExporter());

webApplicationBuilder.Services.AddSingleton<IConfigureOptions<OtlpExporterOptions>>(provider =>
    new ConfigureOptions<OtlpExporterOptions>(new ConfigureOtlp(provider).ConfigureOtlpExporterOptions));

webApplicationBuilder.Services.AddSingleton<IConfigureOptions<OpenTelemetryLoggerOptions>>(provider =>
    new ConfigureOptions<OpenTelemetryLoggerOptions>(options =>
    {
        options.SetResourceBuilder(ResourceBuilder.CreateDefault()
            .AddService(provider.GetRequiredService<IHostEnvironment>().ApplicationName));

        options.IncludeFormattedMessage = true;

        options.AddOtlpExporter(new ConfigureOtlp(provider).ConfigureOtlpExporterOptions);
    }));

var app = webApplicationBuilder.Build();

app.Map("/", static context =>
{
    if (Activity.Current is { } activity)
        context.Response.Headers["x-trace-id"] = activity.TraceId.ToString();

    return context.RequestServices.GetRequiredService<DemoClass>().Demo();
});

app.Services.GetRequiredService<MeterProvider>();
app.Services.GetRequiredService<TracerProvider>();

app.Run();

record ConfigureOtlp(IServiceProvider Services)
{
    public void ConfigureResource(ResourceBuilder builder) =>
        builder.AddService(Services.GetRequiredService<IHostEnvironment>().ApplicationName);

    public void ConfigureOtlpExporterOptions(OtlpExporterOptions options)
    {
        var configuration = Services.GetRequiredService<IConfiguration>();

        options.Endpoint = new(configuration["otlp:endpoint"]!);
        options.Headers = new(configuration["otlp:headers"]!);
    }
}
