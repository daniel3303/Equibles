using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class AumSnapshotRebuildWorkerBackfillCommandTimeoutTests
{
    // AumSnapshotRebuildWorker.BackfillCommandTimeout is the extended
    // CommandTimeout applied to the one-off first-boot backfill — the
    // worker's docstring explicitly notes the default Npgsql 30s ceiling
    // is too tight for the multi-distinct GROUP BY over ~5–15M rows per
    // quarter × ~100 quarters that the first sweep has to scan. The 30
    // minute value was chosen to absorb the longest-observed cold backfill
    // duration with margin (real production runs against a stale db land
    // in the 5–20min range; 30min is "comfortably above worst").
    //
    // The risk this catches:
    //   • A "harmonise with SleepInterval" refactor that drops the
    //     extended timeout in favour of the default — first-boot
    //     backfill would 30s-timeout in the middle of the multi-distinct
    //     scan, leaving the AumQuarterlySnapshot table partially populated
    //     and silently incorrect on /holdings/stats.
    //   • A typo (`FromSeconds(30)` from a copy-paste of the default) —
    //     same outcome, different cause.
    //   • A "tighten — 30 min is excessive" cleanup — risks bringing
    //     back the first-boot failure mode the property was introduced
    //     to fix.
    //
    // Property is `protected virtual` so a test-only subclass exposes
    // it without needing reflection. Pin the literal 30-minute value
    // so any future change must update this test deliberately.
    [Fact]
    public void BackfillCommandTimeout_IsThirtyMinutes()
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

        sut.InvokeBackfillCommandTimeout().Should().Be(TimeSpan.FromMinutes(30));
    }

    private sealed class TestableAumSnapshotRebuildWorker : AumSnapshotRebuildWorker
    {
        public TestableAumSnapshotRebuildWorker(
            IServiceScopeFactory scopeFactory,
            HoldingsAggregateRefreshService refreshService,
            ILogger<AumSnapshotRebuildWorker> logger
        )
            : base(scopeFactory, refreshService, logger) { }

        public TimeSpan InvokeBackfillCommandTimeout() => BackfillCommandTimeout;
    }
}
