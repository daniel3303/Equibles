using System.Net;
using Equibles.Core.AutoWiring;
using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.EquityMarkets.HostedService.Services;
using Equibles.Integrations.Bme;
using Equibles.Integrations.Esma;
using Equibles.Integrations.Euronext;
using Equibles.Integrations.Gleif;
using Equibles.Integrations.Gpw;
using Equibles.Integrations.Lse;
using Equibles.Integrations.NasdaqNordic;
using Equibles.Integrations.Xetra;

namespace Equibles.EquityMarkets.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEquityMarketsWorker(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<EquityMarketDirectoryImporter>();

        AddSourceClient<EuronextDirectoryClient>(services);
        AddSourceClient<GleifIdentityClient>(services);
        AddSourceClient<XetraInstrumentListClient>(services);
        AddSourceClient<NasdaqNordicClient>(services);
        AddSourceClient<BmeClient>(services);
        AddSourceClient<GpwClient>(services);
        AddSourceClient<LseInstrumentListClient>(services);
        AddSourceClient<EsmaFirdsClient>(services, TimeSpan.FromMinutes(20));
        AddSourceClient<FcaFirdsClient>(services, TimeSpan.FromMinutes(20));
        services.AddTransient<IFirdsFileIndex>(provider =>
            provider.GetRequiredService<EsmaFirdsClient>()
        );
        services.AddTransient<IFirdsFileIndex>(provider =>
            provider.GetRequiredService<FcaFirdsClient>()
        );

        services.AddScoped<IEquityMarketDirectorySource, EuronextEquityMarketDirectorySource>();
        services.AddScoped<IEquityMarketDirectorySource, XetraEquityMarketDirectorySource>();
        services.AddScoped<IEquityMarketDirectorySource, NasdaqNordicEquityMarketDirectorySource>();
        services.AddScoped<IEquityMarketDirectorySource, BmeEquityMarketDirectorySource>();
        services.AddScoped<IEquityMarketDirectorySource, GpwEquityMarketDirectorySource>();
        services.AddScoped<IEquityMarketDirectorySource, LseEquityMarketDirectorySource>();

        services.AddHostedService<FirdsUniverseWorker>();
        services.AddHostedService<EquityMarketDirectoryWorker>();
        return services;
    }

    // No source is ever followed off its origin; each client checks the final request URI itself.
    private static void AddSourceClient<TClient>(
        IServiceCollection services,
        TimeSpan? timeout = null
    )
        where TClient : class =>
        services
            .AddHttpClient<TClient>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Equibles/1.0");
                if (timeout != null)
                    client.Timeout = timeout.Value;
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.All,
                }
            );
}
