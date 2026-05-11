using Equibles.Core.AutoWiring;
using Equibles.Core.Contracts;
using Equibles.Yahoo.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Yahoo.HostedService.Extensions;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddYahooWorker(this IServiceCollection services) {
        services.AutoWireServicesFrom<YahooPriceImportService>();
        services.AutoWireServicesFrom<Equibles.Integrations.Yahoo.YahooFinanceClient>();

        // Yahoo is the OSS source of stock prices for Holdings valuation.
        services.AddScoped<IStockPriceProvider, YahooStockPriceProvider>();

        services.AddHostedService<YahooPriceScraperWorker>();

        return services;
    }
}
