using Equibles.CommonStocks.BusinessLogic.Websites;
using Equibles.Core.AutoWiring;
using Equibles.Core.Contracts;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.Yahoo.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Yahoo.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddYahooWorker(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<YahooPriceImportService>();
        services.AutoWireServicesFrom<Equibles.Integrations.Yahoo.YahooFinanceClient>();

        // The price import piggybacks split and dividend capture on its chart
        // fetch; auto-wire the CorporateActions.BusinessLogic assembly (both
        // capture managers) so they resolve in the import scope. Their
        // repositories are picked up by AddAllRepositories (PluginLoader loads
        // Equibles.CorporateActions.Repositories.dll from the output dir).
        services.AutoWireServicesFrom<StockSplitCaptureManager>();

        // Yahoo is the OSS source of stock prices for Holdings valuation.
        services.AddScoped<IStockPriceProvider, YahooStockPriceProvider>();

        // Last-resort IWebsiteSource (consumed by the CommonStocks website
        // discovery worker): the Yahoo asset profile, for the long tail the
        // filings and Wikidata sources leave unfilled.
        services.AddScoped<IWebsiteSource, YahooWebsiteSource>();

        services.AddHostedService<YahooPriceScraperWorker>();

        return services;
    }
}
