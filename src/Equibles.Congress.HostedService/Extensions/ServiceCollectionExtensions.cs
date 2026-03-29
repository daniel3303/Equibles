using Equibles.Congress.HostedService.Services;
using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Congress.HostedService.Extensions;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddCongressWorker(this IServiceCollection services) {
        services.AutoWireServicesFrom<CongressionalTradeSyncService>();

        services.AddHostedService<CongressionalTradeScraperWorker>();

        return services;
    }
}
