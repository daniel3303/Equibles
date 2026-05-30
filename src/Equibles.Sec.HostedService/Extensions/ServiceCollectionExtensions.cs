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
        services.AddScoped<IDocumentScraper, DocumentScraper>();

        services.AddHostedService<SecScraperWorker>();
        services.AddHostedService<DocumentProcessorWorker>();
        services.AddHostedService<FtdScraperWorker>();
        services.AddHostedService<FormAdvScraperWorker>();

        return services;
    }
}
