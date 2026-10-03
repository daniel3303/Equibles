using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

// The daily safety-net cycle restarts on every boot; this pins the rule that lets a boot
// skip it (every recent quarter rebuilt inside the window and none dirty) and the wake-up
// that keeps the cadence daily afterwards.
public class AumSnapshotRebuildWorkerFreshnessTests
{
    private static readonly DateOnly Q3 = new(2024, 9, 30);
    private static readonly DateOnly Q4 = new(2024, 12, 31);
    private static readonly DateTime Now = new(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromHours(20);

    [Fact]
    public void OldestFreshRebuild_EveryQuarterRebuiltInsideTheWindow_SkipsAndReportsTheOldest()
    {
        var oldest = AumSnapshotRebuildWorker.OldestFreshRebuild(
            [Q4, Q3],
            [Snapshot(Q4, Now.AddHours(-1)), Snapshot(Q3, Now.AddHours(-19))],
            Now,
            Window
        );

        oldest.Should().Be(Now.AddHours(-19));
    }

    [Fact]
    public void OldestFreshRebuild_NoQuartersOnFile_Skips()
    {
        AumSnapshotRebuildWorker.OldestFreshRebuild([], [], Now, Window).Should().Be(Now);
    }

    [Fact]
    public void OldestFreshRebuild_MissingQuarter_Rebuilds()
    {
        var oldest = AumSnapshotRebuildWorker.OldestFreshRebuild(
            [Q4, Q3],
            [Snapshot(Q4, Now.AddHours(-1))],
            Now,
            Window
        );

        oldest.Should().BeNull();
    }

    [Fact]
    public void OldestFreshRebuild_StaleQuarter_Rebuilds()
    {
        var oldest = AumSnapshotRebuildWorker.OldestFreshRebuild(
            [Q4, Q3],
            [Snapshot(Q4, Now.AddHours(-1)), Snapshot(Q3, Now.AddHours(-21))],
            Now,
            Window
        );

        oldest.Should().BeNull();
    }

    [Fact]
    public void OldestFreshRebuild_DirtyQuarter_Rebuilds()
    {
        var dirty = Snapshot(Q4, Now.AddHours(-1));
        dirty.DirtyAt = Now.AddMinutes(-5);

        var oldest = AumSnapshotRebuildWorker.OldestFreshRebuild(
            [Q4, Q3],
            [dirty, Snapshot(Q3, Now.AddHours(-1))],
            Now,
            Window
        );

        oldest.Should().BeNull();
    }

    [Fact]
    public void NextCycleDelay_WakesWhenTheOldestRebuildTurnsACycleOld()
    {
        var sleep = TimeSpan.FromHours(24);

        AumSnapshotRebuildWorker.NextCycleDelay(Now.AddHours(-19), Now, sleep).Should().Be(TimeSpan.FromHours(5));
        AumSnapshotRebuildWorker.NextCycleDelay(Now.AddHours(-25), Now, sleep).Should().Be(TimeSpan.Zero);
    }

    // The window must stay inside the sleep, or a long-lived process would alternate skip and
    // rebuild cycles and halve the cadence.
    [Fact]
    public void FreshnessWindow_IsTwentyHoursAndShorterThanTheSleep()
    {
        var sut = new SeamWorker();

        sut.Window.Should().Be(TimeSpan.FromHours(20));
        sut.Window.Should().BeLessThan(sut.Sleep);
    }

    private sealed class SeamWorker : AumSnapshotRebuildWorker
    {
        public SeamWorker()
            : base(
                Substitute.For<IServiceScopeFactory>(),
                new HoldingsAggregateRefreshService(
                    Substitute.For<IServiceScopeFactory>(),
                    Substitute.For<ILogger<HoldingsAggregateRefreshService>>()
                ),
                Substitute.For<ILogger<AumSnapshotRebuildWorker>>()
            ) { }

        public TimeSpan Window => FreshnessWindow;
        public TimeSpan Sleep => SleepInterval;
    }

    private static AumQuarterlySnapshot Snapshot(DateOnly reportDate, DateTime computedAt) =>
        new() { ReportDate = reportDate, ComputedAt = computedAt };
}
