using Equibles.CommonStocks.BusinessLogic.Websites;
using Equibles.CommonStocks.HostedService.Services;
using Equibles.Core.AutoWiring;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Equibles.CommonStocks.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommonStocksWorker(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<InvestorRelationsDiscoveryService>();
        services.AutoWireServicesFrom<Equibles.Integrations.Wikidata.WikidataClient>();

        // Shared, process-wide politeness gate for outbound scraping (per-host throttle + rate-limit
        // cooldown). A singleton so every scraper lane (IR discovery here, webcast/slides capture,
        // IR-flow) shares one view of each host — the rate-limit ban is IP-wide. TryAdd so a host that
        // also registers it (the commercial worker wires its capture path to the same instance) doesn't
        // create a second.
        services.TryAddSingleton<OutboundHostGate>();

        // Secondary IWebsiteSource: Wikidata's official-website fact joined on the
        // SEC CIK (the filings source registered by the Sec module is the primary).
        services.AddScoped<IWebsiteSource, WikidataWebsiteSource>();

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
        // IR-page discovery probes through the stealth sidecar only (no plain-HTTP client): IR hosts
        // are bot-protected, so plain HTTP only ever returned challenge pages that failed validation.
        services.AddScoped<InvestorRelationsProbeClient>();

        services.AddHostedService<InvestorRelationsDiscoveryWorker>();

        return services;
    }
}
