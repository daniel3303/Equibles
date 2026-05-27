using Equibles.Core.AutoWiring;
using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Holdings.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHoldingsWorker(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<HoldingsImportService>();

        services.AddHostedService<HoldingsScraperWorker>();
        services.AddHostedService<Holdings13FRealtimeWorker>();
        services.AddHostedService<AumSnapshotDrainWorker>();
        services.AddHostedService<AumSnapshotRebuildWorker>();

        return services;
    }
}
