using Castle.DynamicProxy;
using OpenTelemetry.DynamicProxy;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDemoClass(this IServiceCollection services)
    {
        services.AddSingleton<IActivityInvokerFactory, ActivityInvokerFactory>()
            .AddSingleton<ActivityInterceptor>().AddSingleton(static provider =>
                new ProxyGenerator().CreateClassProxy<DemoClass>(provider.GetRequiredService<ActivityInterceptor>()));

        return services;
    }
}
