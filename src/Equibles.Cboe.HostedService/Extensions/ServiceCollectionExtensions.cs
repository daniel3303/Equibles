using Equibles.Cboe.HostedService.Services;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Cboe;
using Equibles.Integrations.Cboe.Contracts;
using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Cboe.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCboeWorker(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<CboeImportService>();

        // CboeClient needs a single rate-limit budget shared across every scope in
        // the host. Capture one limiter here and inject it into the per-scope client.
        var cboeRateLimiter = new RateLimiter(maxRequests: 10, timeWindow: TimeSpan.FromMinutes(1));
        services.AddScoped<ICboeClient>(sp => new CboeClient(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<CboeClient>(),
            cboeRateLimiter
        ));

        services.AddHostedService<CboeScraperWorker>();

        return services;
    }
}
