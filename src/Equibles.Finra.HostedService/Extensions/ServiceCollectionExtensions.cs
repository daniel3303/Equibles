using Equibles.Core.AutoWiring;
using Equibles.Finra.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Equibles.Finra.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFinraWorker(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AutoWireServicesFrom<ShortVolumeImportService>();
        services.AutoWireServicesFrom<Equibles.Integrations.Finra.FinraClient>();

        services.AddHostedService<FinraScraperWorker>();

        return services;
    }
}
