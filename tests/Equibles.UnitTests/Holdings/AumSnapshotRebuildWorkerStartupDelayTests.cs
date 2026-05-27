using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class AumSnapshotRebuildWorkerStartupDelayTests
{
    // Sibling to AumSnapshotRebuildWorkerBackfillCommandTimeoutTests.
    // AumSnapshotRebuildWorker is the daily safety-net for the
    // per-quarter AUM / sector-allocation snapshots that power
    // /holdings/stats and /holdings/trends. ExecuteAsync awaits
    // `Task.Delay(StartupDelay, stoppingToken)` BEFORE the first
    // backfill / rebuild cycle.
    //
    // The 5-minute literal is calibrated against the dependent path:
    // Filings13FImportedConsumer must already be up and processing
    // the live event stream before the safety-net first runs — a
    // FROM=0 startup would race the consumer's initial scope-factory
    // resolution and the first-boot backfill could attempt to read
    // an EquiblesFinancialDbContext before MassTransit's startup
    // probe completes, producing a transient "DbContext disposed"
    // error on every cold start.
    //
    // The risks this pin uniquely catches:
    //   • FromMinutes → FromHours swap. The base BackgroundService
    //     awaits Task.Delay(StartupDelay); a swap turns the
    //     5-minute boot stagger into 5 HOURS of silent delay —
    //     /holdings/stats and /holdings/trends stay frozen on the
    //     previous day's data for the entire first cycle after
    //     deploy with no log signal.
    //   • TimeSpan.Zero from a "skip stagger" refactor. The
    //     race-against-consumer-startup failure mode would return.
    //   • A typo (FromSeconds(5) under the false intuition that
    //     "5 seconds is enough head-start time") would re-introduce
    //     the cold-start race.
    //
    // Property is `protected virtual`; a test-only subclass exposes
    // it for a direct literal pin without reflection.
    [Fact]
    public void StartupDelay_IsFiveMinutes()
    {
        var refreshService = new HoldingsAggregateRefreshService(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<HoldingsAggregateRefreshService>>()
        );
        var sut = new TestableAumSnapshotRebuildWorker(
            Substitute.For<IServiceScopeFactory>(),
            refreshService,
            Substitute.For<ILogger<AumSnapshotRebuildWorker>>()
        );

        sut.InvokeStartupDelay().Should().Be(TimeSpan.FromMinutes(5));
    }

    private sealed class TestableAumSnapshotRebuildWorker : AumSnapshotRebuildWorker
    {
        public TestableAumSnapshotRebuildWorker(
            IServiceScopeFactory scopeFactory,
            HoldingsAggregateRefreshService refreshService,
            ILogger<AumSnapshotRebuildWorker> logger
        )
            : base(scopeFactory, refreshService, logger) { }

        public TimeSpan InvokeStartupDelay() => StartupDelay;
    }
}
