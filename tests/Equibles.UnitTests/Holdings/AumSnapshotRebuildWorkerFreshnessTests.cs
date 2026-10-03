using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

// The daily safety-net cycle restarts on every boot; this pins the rule that lets a boot
// skip it: every recent quarter rebuilt inside the window and none dirty.
public class AumSnapshotRebuildWorkerFreshnessTests
{
    private static readonly DateOnly Q3 = new(2024, 9, 30);
    private static readonly DateOnly Q4 = new(2024, 12, 31);
    private static readonly DateTime Now = new(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromHours(20);

    [Fact]
    public void IsRecentCoverageFresh_EveryQuarterRebuiltInsideTheWindow_Skips()
    {
        var fresh = AumSnapshotRebuildWorker.IsRecentCoverageFresh(
            [Q4, Q3],
            [Snapshot(Q4, Now.AddHours(-1)), Snapshot(Q3, Now.AddHours(-19))],
            Now,
            Window
        );

        fresh.Should().BeTrue();
    }

    [Fact]
    public void IsRecentCoverageFresh_NoQuartersOnFile_Skips()
    {
        AumSnapshotRebuildWorker.IsRecentCoverageFresh([], [], Now, Window).Should().BeTrue();
    }

    [Fact]
    public void IsRecentCoverageFresh_MissingQuarter_Rebuilds()
    {
        var fresh = AumSnapshotRebuildWorker.IsRecentCoverageFresh(
            [Q4, Q3],
            [Snapshot(Q4, Now.AddHours(-1))],
            Now,
            Window
        );

        fresh.Should().BeFalse();
    }

    [Fact]
    public void IsRecentCoverageFresh_StaleQuarter_Rebuilds()
    {
        var fresh = AumSnapshotRebuildWorker.IsRecentCoverageFresh(
            [Q4, Q3],
            [Snapshot(Q4, Now.AddHours(-1)), Snapshot(Q3, Now.AddHours(-21))],
            Now,
            Window
        );

        fresh.Should().BeFalse();
    }

    [Fact]
    public void IsRecentCoverageFresh_DirtyQuarter_Rebuilds()
    {
        var dirty = Snapshot(Q4, Now.AddHours(-1));
        dirty.DirtyAt = Now.AddMinutes(-5);

        var fresh = AumSnapshotRebuildWorker.IsRecentCoverageFresh(
            [Q4, Q3],
            [dirty, Snapshot(Q3, Now.AddHours(-1))],
            Now,
            Window
        );

        fresh.Should().BeFalse();
    }

    private static AumQuarterlySnapshot Snapshot(DateOnly reportDate, DateTime computedAt) =>
        new() { ReportDate = reportDate, ComputedAt = computedAt };
}
