using Equibles.Core.AutoWiring;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Holdings.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHoldingsWorker(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<HoldingsImportService>();
        // The scoring worker's only domain dependency lives in the BusinessLogic assembly;
        // register it here so the worker's composition root is self-contained.
        services.AutoWireServicesFrom<FundScoringManager>();

        services.AddHostedService<HoldingsScraperWorker>();
        services.AddHostedService<Holdings13FRealtimeWorker>();
        services.AddHostedService<Holdings13DGRealtimeWorker>();
        services.AddHostedService<Holdings13FReconciliationWorker>();
        services.AddHostedService<AumSnapshotDrainWorker>();
        services.AddHostedService<AumSnapshotRebuildWorker>();
        services.AddHostedService<FundScoringWorker>();

        return services;
    }
}
