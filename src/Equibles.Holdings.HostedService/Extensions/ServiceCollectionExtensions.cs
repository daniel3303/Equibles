using Equibles.Core.AutoWiring;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Holdings.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHoldingsWorker(this IServiceCollection services)
    {
        // Registers the import/ingestion services (including the reconciliation
        // service) without starting any background loop.
        services.AddHoldingsReconciliation();
        // The scoring worker's only domain dependency lives in the BusinessLogic assembly;
        // register it here so the worker's composition root is self-contained.
        services.AutoWireServicesFrom<FundScoringManager>();

        services.AddHostedService<HoldingsScraperWorker>();
        services.AddHostedService<Holdings13FRealtimeWorker>();
        services.AddHostedService<Holdings13DGRealtimeWorker>();
        services.AddHostedService<AumSnapshotDrainWorker>();
        services.AddHostedService<AumSnapshotRebuildWorker>();
        services.AddHostedService<FundScoringWorker>();

        return services;
    }

    /// <summary>
    /// Registers the Holdings ingestion services — the shared import path and the
    /// on-demand <see cref="Holdings13FReconciliationService"/> — as scoped
    /// services, without registering any background worker. Web hosts (e.g. the
    /// Backoffice, which drives reconciliation from a button) call this so the
    /// reconciliation service is resolvable outside the worker process.
    /// </summary>
    public static IServiceCollection AddHoldingsReconciliation(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<HoldingsImportService>();
        return services;
    }
}
