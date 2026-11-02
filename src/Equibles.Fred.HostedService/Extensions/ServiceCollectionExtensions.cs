using Equibles.Core.AutoWiring;
using Equibles.Fred.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Fred.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFredWorker(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<FredImportService>();
        services.AutoWireServicesFrom<Equibles.Integrations.Fred.FredClient>();

        services.AddHostedService<FredScraperWorker>();
        services.AddHttpClient(
            Canada.CanadianIndicatorImportService.ClientName,
            client =>
            {
                client.Timeout = TimeSpan.FromMinutes(2);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Equibles/1.0");
            }
        );
        services.AddHostedService<Canada.CanadianIndicatorWorker>();

        return services;
    }
}
