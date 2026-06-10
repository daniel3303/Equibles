using Equibles.Core.AutoWiring;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Sec.FinancialFacts.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSecFinancialFactsWorker(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<FinancialFactsImportService>();
        // The XBRL parsers live in the BusinessLogic assembly; wire them so the
        // dimensional-fact extraction sweep can resolve them.
        services.AutoWireServicesFrom<InlineXbrlParser>();
        services.AddHostedService<FinancialFactsScraperWorker>();
        services.AddHostedService<XbrlFactsExtractionWorker>();
        return services;
    }
}
