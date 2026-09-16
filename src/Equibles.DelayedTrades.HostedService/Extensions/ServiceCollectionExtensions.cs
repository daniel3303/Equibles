using System.Net;
using Equibles.Core.AutoWiring;
using Equibles.DelayedTrades.BusinessLogic.Import;
using Equibles.Integrations.DelayedTrades;
using Equibles.Integrations.Euronext.DelayedTrades;

namespace Equibles.DelayedTrades.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDelayedTradesWorker(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<DelayedTradeImportService>();
        AddSourceClient<EuronextDelayedTradeSource>(services, TimeSpan.FromMinutes(3));
        services.AddTransient<IDelayedTradeSource>(provider =>
            provider.GetRequiredService<EuronextDelayedTradeSource>()
        );
        services.AddHostedService<DelayedTradeScraperWorker>();
        return services;
    }

    // Never followed off its origin; the client checks the final request URI itself.
    private static void AddSourceClient<TClient>(IServiceCollection services, TimeSpan timeout)
        where TClient : class =>
        services
            .AddHttpClient<TClient>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Equibles/1.0");
                client.Timeout = timeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.All,
                }
            );
}
