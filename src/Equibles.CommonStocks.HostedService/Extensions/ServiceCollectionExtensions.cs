using Equibles.CommonStocks.HostedService.Services;
using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.CommonStocks.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommonStocksWorker(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<InvestorRelationsDiscoveryService>();

        // Typed client for website reachability probes: short timeout, contact
        // User-Agent, capped response size (the body is discarded; only the status
        // matters).
        services.AddHttpClient<WebsiteProbeClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "EquiblesBot/1.0 (+https://equibles.com)"
            );
            client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
        });

        // Runs upstream of IR discovery: fills CommonStock.Website from the
        // registered IWebsiteSource implementations (filings, Wikidata, Yahoo, ...).
        services.AddHostedService<WebsiteDiscoveryWorker>();

        // Typed client: short timeout and a contact User-Agent, following many
        // redirects to land on the real IR page. Response size is capped so a
        // misbehaving host can't stream an unbounded body into memory.
        services.AddHttpClient<InvestorRelationsProbeClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "EquiblesBot/1.0 (+https://equibles.com)"
            );
            client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
        });

        services.AddHostedService<InvestorRelationsDiscoveryWorker>();

        // Typed client for the Nasdaq IR Insight RSS feeds: short timeout, contact
        // User-Agent, capped response size. The [Service] scraper itself is picked up
        // by the AutoWireServicesFrom scan above (same assembly).
        services.AddHttpClient<NasdaqIrInsightFeedClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "EquiblesBot/1.0 (+https://equibles.com)"
            );
            client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
        });

        services.AddHostedService<NasdaqIrInsightScraperWorker>();

        // Typed client for the Q4 Inc RSS feeds, configured the same way as the
        // Nasdaq IR Insight client above.
        services.AddHttpClient<Q4IncFeedClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "EquiblesBot/1.0 (+https://equibles.com)"
            );
            client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
        });

        services.AddHostedService<Q4IncScraperWorker>();

        return services;
    }
}
