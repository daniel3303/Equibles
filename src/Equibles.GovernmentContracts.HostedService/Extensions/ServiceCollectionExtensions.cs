using Equibles.Core.AutoWiring;
using Equibles.GovernmentContracts.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.GovernmentContracts.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGovernmentContractsWorker(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<GovernmentContractsImportService>();
        services.AutoWireServicesFrom<Equibles.Integrations.GovernmentContracts.UsaSpendingClient>();

        services.AddHostedService<GovernmentContractsScraperWorker>();

        return services;
    }
}
