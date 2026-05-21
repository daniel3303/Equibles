using System.Reflection;
using Equibles.Holdings.Repositories;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class HoldingsBacktestServiceSelectRelevantSnapshotsPostWindowTests
{
    private static readonly MethodInfo SelectRelevantSnapshotDatesMethod =
        typeof(HoldingsBacktestService).GetMethod(
            "SelectRelevantSnapshotDates",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // Third arm of SelectRelevantSnapshotDates' contract — "pick all snapshots
    // whose rebalance date falls in [resolvedFrom, resolvedTo], plus the latest
    // one whose rebalance date precedes resolvedFrom." A snapshot whose
    // rebalance date sits strictly AFTER resolvedTo must be dropped: it
    // represents a 13F filing the cloner could not yet have read at the
    // simulated time, so including it would inject lookahead bias into the
    // backtest. The sibling Boundary and AllPreWindow pins cover the
    // equals-from edge and the all-pre-window opener arm respectively — the
    // post-window drop has no dedicated pin, so a refactor that changed
    // `else if (rebalance <= resolvedTo)` to a bare `else` (or to `>= `) would
    // start sending future filings into the simulation, silently turning the
    // backtest into peeking. Construct one snapshot in each region and assert
    // the post-window one never appears in the result.
    [Fact]
    public void SelectRelevantSnapshotDates_RebalanceStrictlyAfterResolvedTo_NotIncluded()
    {
        var resolvedFrom = new DateOnly(2024, 3, 15);
        var resolvedTo = new DateOnly(2024, 6, 30);
        var rebalanceDelay = HoldingsBacktestCalculator.RebalanceDelayDays;

        // Pre-window opener: rebalance = 2024-02-14 (< resolvedFrom).
        var preWindow = resolvedFrom.AddDays(-30 - rebalanceDelay);
        // In-window: rebalance = 2024-04-29 (resolvedFrom < r <= resolvedTo).
        var inWindow = resolvedFrom.AddDays(-rebalanceDelay).AddDays(45);
        // Post-window: rebalance = 2024-08-29 (> resolvedTo).
        var postWindow = resolvedTo.AddDays(60 - rebalanceDelay);

        (preWindow.AddDays(rebalanceDelay) < resolvedFrom).Should().BeTrue();
        (inWindow.AddDays(rebalanceDelay) >= resolvedFrom).Should().BeTrue();
        (inWindow.AddDays(rebalanceDelay) <= resolvedTo).Should().BeTrue();
        (postWindow.AddDays(rebalanceDelay) > resolvedTo).Should().BeTrue();

        IReadOnlyList<DateOnly> reportDates = [preWindow, inWindow, postWindow];

        var result =
            (List<DateOnly>)
                SelectRelevantSnapshotDatesMethod.Invoke(
                    null,
                    [reportDates, resolvedFrom, resolvedTo]
                );

        result.Should().Equal(preWindow, inWindow);
        result.Should().NotContain(postWindow);
    }
}
