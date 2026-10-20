using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

public class Holdings13DGCoverageWorkerComputeWalkWindowTests
{
    [Fact]
    public void ComputeWalkWindow_RealtimeSweepNeverRan_WalksNothing()
    {
        Holdings13DGCoverageWorker.ComputeWalkWindow(null, null).Should().BeNull();
    }

    [Fact]
    public void ComputeWalkWindow_NoCursor_StartsWithThePreXmlFilingsAndStopsBeforeTheRealtimeWindow()
    {
        Holdings13DGCoverageWorker
            .ComputeWalkWindow(null, new DateOnly(2026, 10, 20))
            .Should()
            .Be((new DateOnly(2023, 10, 20), new DateOnly(2026, 10, 5)));
    }

    [Fact]
    public void ComputeWalkWindow_Cursor_ResumesTheDayAfter()
    {
        Holdings13DGCoverageWorker
            .ComputeWalkWindow(new DateOnly(2025, 2, 14), new DateOnly(2026, 10, 20))
            .Should()
            .Be((new DateOnly(2025, 2, 15), new DateOnly(2026, 10, 5)));
    }

    [Fact]
    public void ComputeWalkWindow_CursorCaughtUpWithTheRealtimeWindow_WalksNothing()
    {
        Holdings13DGCoverageWorker
            .ComputeWalkWindow(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 20))
            .Should()
            .BeNull();
    }

    [Fact]
    public void ComputeWalkWindow_RealtimeWatermarkAdvances_WalksTheDayThatLeftItsWindow()
    {
        Holdings13DGCoverageWorker
            .ComputeWalkWindow(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 21))
            .Should()
            .Be((new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 6)));
    }
}
