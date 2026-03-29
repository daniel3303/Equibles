using Equibles.Core.AutoWiring;
using Equibles.Yahoo.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Yahoo.HostedService.Extensions;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddYahooWorker(this IServiceCollection services) {
        services.AutoWireServicesFrom<YahooPriceImportService>();
        services.AutoWireServicesFrom<Equibles.Integrations.Yahoo.YahooFinanceClient>();

        services.AddHostedService<YahooPriceScraperWorker>();

        return services;
    }
}
