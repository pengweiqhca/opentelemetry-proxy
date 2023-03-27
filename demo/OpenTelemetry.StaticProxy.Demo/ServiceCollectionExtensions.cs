// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDemoClass(this IServiceCollection services)
    {
        services.AddSingleton<DemoClass>();

        return services;
    }
}
