using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Proxy.Demo;
using OpenTelemetry.Resources;
using OpenTelemetry.Proxy.Generated;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddDemoClass()
    .AddLogging(static lb => lb.AddOpenTelemetry())
    .AddOpenTelemetry()
    .WithMetrics(static builder => ConfigureOtlp.ConfigureResource(builder).AddOtlpExporter())
    .WithTracing(static builder => ConfigureOtlp.ConfigureResource(builder)
        .AddAspNetCoreInstrumentation()
        .AddOpenTelemetryProxyDemoSources()
        .AddOtlpExporter());

builder.Services.AddSingleton<IConfigureOptions<OpenTelemetryLoggerOptions>>(provider =>
    new ConfigureOptions<OpenTelemetryLoggerOptions>(options =>
    {
        options.SetResourceBuilder(ResourceBuilder.CreateDefault()
            .AddService(provider.GetRequiredService<IHostEnvironment>().ApplicationName));

        options.IncludeFormattedMessage = true;

        options.AddOtlpExporter();
    }));

var app = builder.Build();

app.Map("/", static async context =>
{
    if (Activity.Current is { } activity)
    {
        context.Response.Headers["x-trace-id"] = activity.Id;

        var values = context.Request.Query["traceParent"];
        if (values.Count > 0 && ActivityContext.TryParse(values[0], null, out var activityContext))
            activity.AddLink(new(activityContext, new() { { "abc", "def" }, { "now", DateTime.Now } }));
    }

    await context.Response.WriteAsJsonAsync(await context.RequestServices.GetRequiredService<DemoClass>().Demo(DateTime.Now.Second).ConfigureAwait(false)).ConfigureAwait(false);
});

app.Services.GetRequiredService<MeterProvider>();
app.Services.GetRequiredService<TracerProvider>();

app.Run();
