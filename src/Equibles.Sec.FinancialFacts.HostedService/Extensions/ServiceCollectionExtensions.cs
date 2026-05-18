using Equibles.Core.AutoWiring;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Sec.FinancialFacts.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSecFinancialFactsWorker(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<FinancialFactsImportService>();
        services.AddHostedService<FinancialFactsScraperWorker>();
        return services;
    }
}
