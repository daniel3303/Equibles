using Equibles.Cboe.HostedService.Services;
using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Cboe.HostedService.Extensions;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddCboeWorker(this IServiceCollection services) {
        services.AutoWireServicesFrom<CboeImportService>();
        services.AutoWireServicesFrom<Equibles.Integrations.Cboe.CboeClient>();

        services.AddHostedService<CboeScraperWorker>();

        return services;
    }
}
