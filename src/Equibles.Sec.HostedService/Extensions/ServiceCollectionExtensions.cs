using System.Net;
using Equibles.CommonStocks.BusinessLogic.Websites;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.XbrlFilings;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Equibles.Sec.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSecWorker(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AutoWireServicesFrom<DocumentManager>();
        services.AutoWireServicesFrom<Equibles.Integrations.Sec.SecEdgarClient>();

        // The European filing index and the reports it addresses. The client paces itself and never follows
        // a redirect off its origin; the timeout is generous because one report ran to 125 MB.
        services
            .AddHttpClient<XbrlFilingsClient>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Equibles/1.0");
                client.Timeout = TimeSpan.FromMinutes(20);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.All,
                }
            );

        services.AddScoped<IFilingProcessor, InsiderTradingFilingProcessor>();
        services.AddScoped<IFilingProcessor, Form144FilingProcessor>();
        services.AddScoped<IFilingProcessor, FormDFilingProcessor>();
        services.AddScoped<IFilingProcessor, NCenFilingProcessor>();
        services.AddScoped<IFilingProcessor, NportFilingProcessor>();
        services.AddScoped<IDocumentPersistenceService, DocumentPersistenceService>();
        services.AddScoped<ICompanySyncService, CompanySyncService>();
        services.AddScoped<XbrlEnvelopeCaptureService>();
        services.AddScoped<AsFiledHtmlCaptureService>();
        services.AddScoped<AsFiledHtmlBackfillService>();
        services.AddScoped<DocumentNormalizationBackfillService>();
        services.AddScoped<DocumentImageService>();
        services.AddScoped<FilingItemsBackfillService>();
        services.AddScoped<IDocumentScraper, DocumentScraper>();
        services.AddScoped<IFilingDiscoveryService, FilingDiscoveryService>();
        // Primary IWebsiteSource (consumed by the CommonStocks website discovery
        // worker): the website disclosure mandated in the stocks' own stored filings.
        services.AddScoped<IWebsiteSource, FilingsWebsiteSource>();

        services.AddHostedService<SecScraperWorker>();
        services.AddHostedService<DocumentProcessorWorker>();
        services.AddHostedService<FtdScraperWorker>();
        services.AddHostedService<FormAdvScraperWorker>();
        services.AddHostedService<AsFiledHtmlBackfillWorker>();
        services.AddHostedService<DocumentNormalizationBackfillWorker>();
        services.AddHostedService<FilingItemsBackfillWorker>();
        services.AddHostedService<InsiderFilingReprocessWorker>();
        services.AddHostedService<Form144FilerCikBackfillWorker>();
        services.AddHostedService<NportFilingReprocessWorker>();
        services.AddHostedService<NportRealtimeWorker>();
        services.AddHostedService<FundSeriesRefreshWorker>();
        services.AddHostedService<EsefReportScraperWorker>();

        return services;
    }
}
