using Equibles.Core.AutoWiring;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Intelligence.Benchmarks;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.HostedService.Services.Benchmarks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        services.AddHolderBenchmarks();

        services.AddHostedService<HoldingsScraperWorker>();
        services.AddHostedService<Holdings13FRealtimeWorker>();
        services.AddHostedService<Holdings13DGRealtimeWorker>();
        services.AddHostedService<BeneficialOwnershipDetailSyncWorker>();
        services.AddHostedService<Holdings13DGCoverageWorker>();
        services.AddHostedService<AumSnapshotDrainWorker>();
        services.AddHostedService<HolderIntelligenceDrainWorker>();
        services.AddHostedService<AumSnapshotRebuildWorker>();
        services.AddHostedService<FundScoringWorker>();

        return services;
    }

    /// <summary>
    /// Registers the benchmarks the intelligence drain measures active share against: the combined
    /// 13F book and index-tracking funds' N-PORT holdings (<see cref="HolderBenchmarkOptions"/>).
    /// A host adds other <see cref="IHolderBenchmarkProvider"/> sources with <c>TryAddEnumerable</c>.
    /// </summary>
    public static IServiceCollection AddHolderBenchmarks(this IServiceCollection services)
    {
        services
            .AddOptions<HolderBenchmarkOptions>()
            .BindConfiguration(HolderBenchmarkOptions.SectionName);
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IHolderBenchmarkProvider, AggregateHolderBenchmarkProvider>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IHolderBenchmarkProvider, NportSeriesHolderBenchmarkProvider>()
        );
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
