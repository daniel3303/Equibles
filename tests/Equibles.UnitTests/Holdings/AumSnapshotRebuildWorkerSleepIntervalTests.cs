using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class AumSnapshotRebuildWorkerSleepIntervalTests
{
    // Completes the StartupDelay / BackfillCommandTimeout / SleepInterval
    // property triple for AumSnapshotRebuildWorker. The 24h cadence is the
    // "safety-net" interval — the event-driven path
    // (Filings13FImportedConsumer) keeps snapshots tight on every 13F
    // import; this worker is the second line of defence that reconciles
    // any message lost to a transient bus failure within 24h.
    //
    // The risks this pin uniquely catches:
    //   • FromHours → FromMinutes / FromSeconds swap. The base
    //     BackgroundService loops Task.Delay(SleepInterval); a swap
    //     turns the daily reconcile into a tight loop that re-runs the
    //     full RebuildAllAsync (per-quarter scope creation × ~100
    //     quarters × multi-distinct GROUP BY) every minute or second,
    //     burning DB compute and lock contention against the live
    //     event-driven path.
    //   • FromDays(7) "lighter cadence" cleanup — a message lost to a
    //     transient bus failure would stay unreconciled for a week,
    //     defeating the "second line of defence within 24h" promise
    //     written into the class doc.
    //
    // Property is `protected virtual`; a test-only subclass exposes it
    // for a direct literal pin without reflection — same shape as the
    // sibling StartupDelay / BackfillCommandTimeout pins.
    [Fact]
    public void SleepInterval_IsTwentyFourHours()
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

        sut.InvokeSleepInterval().Should().Be(TimeSpan.FromHours(24));
    }

    private sealed class TestableAumSnapshotRebuildWorker : AumSnapshotRebuildWorker
    {
        public TestableAumSnapshotRebuildWorker(
            IServiceScopeFactory scopeFactory,
            HoldingsAggregateRefreshService refreshService,
            ILogger<AumSnapshotRebuildWorker> logger
        )
            : base(scopeFactory, refreshService, logger) { }

        public TimeSpan InvokeSleepInterval() => SleepInterval;
    }
}
