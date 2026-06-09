using Equibles.CommonStocks.HostedService.Services;
using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.CommonStocks.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommonStocksWorker(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<InvestorRelationsDiscoveryService>();

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

        return services;
    }
}
