using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Sec.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Integrations.Sec.Extensions;

public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the SecEdgar integration services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSecEdgarClient(this IServiceCollection services) {
        services.AddHttpClient("SecEdgarClient", client => {
            client.BaseAddress = new Uri("https://www.sec.gov/edgar/");
        });

        services.AddScoped<ISecEdgarClient, SecEdgarClient>();
        services.AddScoped<IRateLimiter, RateLimiter>();

        return services;
    }

}