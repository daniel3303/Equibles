using Equibles.Core.AutoWiring;
using Equibles.GovernmentContracts.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Equibles.GovernmentContracts.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGovernmentContractsWorker(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<GovernmentContractsImportService>();
        services.AutoWireServicesFrom<Equibles.Integrations.GovernmentContracts.UsaSpendingClient>();

        services.AddHostedService<GovernmentContractsScraperWorker>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(
            Canada.CanadianContractsImportService.ClientName,
            client =>
            {
                client.Timeout = TimeSpan.FromMinutes(2);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Equibles/1.0 (+https://equibles.com)"
                );
            }
        );
        services.AddHostedService<Canada.CanadianContractsWorker>();

        return services;
    }
}
