using Equibles.CommonStocks.BusinessLogic.Websites;
using Equibles.Core.AutoWiring;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Sec.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSecWorker(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<DocumentManager>();
        services.AutoWireServicesFrom<Equibles.Integrations.Sec.SecEdgarClient>();

        services.AddScoped<IFilingProcessor, InsiderTradingFilingProcessor>();
        services.AddScoped<IFilingProcessor, Form144FilingProcessor>();
        services.AddScoped<IFilingProcessor, FormDFilingProcessor>();
        services.AddScoped<IFilingProcessor, NCenFilingProcessor>();
        services.AddScoped<IFilingProcessor, NportFilingProcessor>();
        services.AddScoped<IDocumentPersistenceService, DocumentPersistenceService>();
        services.AddScoped<ICompanySyncService, CompanySyncService>();
        services.AddScoped<XbrlEnvelopeCaptureService>();
        services.AddScoped<XbrlBackfillService>();
        services.AddScoped<FilingItemsBackfillService>();
        services.AddScoped<IDocumentScraper, DocumentScraper>();
        // Primary IWebsiteSource (consumed by the CommonStocks website discovery
        // worker): the website disclosure mandated in the stocks' own stored filings.
        services.AddScoped<IWebsiteSource, FilingsWebsiteSource>();

        services.AddHostedService<SecScraperWorker>();
        services.AddHostedService<DocumentProcessorWorker>();
        services.AddHostedService<FtdScraperWorker>();
        services.AddHostedService<FormAdvScraperWorker>();
        services.AddHostedService<XbrlBackfillWorker>();
        services.AddHostedService<FilingItemsBackfillWorker>();
        services.AddHostedService<InsiderFilingReprocessWorker>();
        services.AddHostedService<NportFilingReprocessWorker>();
        services.AddHostedService<NportRealtimeWorker>();

        return services;
    }
}
