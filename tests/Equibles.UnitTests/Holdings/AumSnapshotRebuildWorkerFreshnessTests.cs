using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

// The daily safety-net cycle restarts on every boot; this pins the rule that lets a boot
// skip it (every recent quarter rebuilt inside the window, dirty or not) and the wake-up
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

    // A dirty quarter belongs to the drain, which clears the flag after its cooldown; the
    // safety-net rebuild never clears it.
    [Fact]
    public void OldestFreshRebuild_DirtyButRecentlyRebuiltQuarter_LeavesItToTheDrain()
    {
        var dirty = Snapshot(Q4, Now.AddHours(-1));
        dirty.DirtyAt = Now.AddMinutes(-5);

        var oldest = AumSnapshotRebuildWorker.OldestFreshRebuild(
            [Q4, Q3],
            [dirty, Snapshot(Q3, Now.AddHours(-2))],
            Now,
            Window
        );

        oldest.Should().Be(Now.AddHours(-2));
    }

    [Fact]
    public void OldestFreshRebuild_DirtyAndStaleQuarter_Rebuilds()
    {
        var dirty = Snapshot(Q4, Now.AddHours(-21));
        dirty.DirtyAt = Now.AddMinutes(-5);

        var oldest = AumSnapshotRebuildWorker.OldestFreshRebuild(
            [Q4, Q3],
            [dirty, Snapshot(Q3, Now.AddHours(-1))],
            Now,
            Window
        );

        oldest.Should().BeNull();
    }

    // A quarter's first import inserts a zero-aggregate stub stamped at its event time; the
    // drain rebuilds it after the cooldown whether or not a boot happens in between.
    [Fact]
    public void OldestFreshRebuild_ConsumerStub_WaitsForTheDrain()
    {
        var stub = Snapshot(Q4, Now.AddMinutes(-10));
        stub.DirtyAt = stub.ComputedAt;

        var oldest = AumSnapshotRebuildWorker.OldestFreshRebuild(
            [Q4, Q3],
            [stub, Snapshot(Q3, Now.AddHours(-1))],
            Now,
            Window
        );

        oldest.Should().Be(Now.AddHours(-1));
    }

    [Fact]
    public void NextCycleDelay_WakesWhenTheOldestRebuildTurnsACycleOld()
    {
        var sleep = TimeSpan.FromHours(24);

        AumSnapshotRebuildWorker
            .NextCycleDelay(Now.AddHours(-19), Now, sleep)
            .Should()
            .Be(TimeSpan.FromHours(5));
        AumSnapshotRebuildWorker
            .NextCycleDelay(Now.AddHours(-25), Now, sleep)
            .Should()
            .Be(TimeSpan.Zero);
        AumSnapshotRebuildWorker
            .NextCycleDelay(Now, Now, sleep)
            .Should()
            .Be(sleep, "no quarters on file sleeps a full cycle");
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
