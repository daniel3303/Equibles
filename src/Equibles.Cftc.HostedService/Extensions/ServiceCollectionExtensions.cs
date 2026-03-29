using Equibles.Cftc.HostedService.Services;
using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Cftc.HostedService.Extensions;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddCftcWorker(this IServiceCollection services) {
        services.AutoWireServicesFrom<CftcImportService>();
        services.AutoWireServicesFrom<Equibles.Integrations.Cftc.CftcClient>();

        services.AddHostedService<CftcScraperWorker>();

        return services;
    }
}
