using System.Reflection;
using Equibles.Holdings.Repositories;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class HoldingsBacktestServiceSelectRelevantSnapshotsAllPreWindowTests
{
    private static readonly MethodInfo SelectRelevantSnapshotDatesMethod =
        typeof(HoldingsBacktestService).GetMethod(
            "SelectRelevantSnapshotDates",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    [Fact]
    public void SelectRelevantSnapshotDates_AllRebalanceDatesPrecedeWindow_ReturnsOnlyMostRecent()
    {
        // Sibling pin to SelectRelevantSnapshotsBoundaryTests, which exercises
        // the in-window boundary (rebalance == resolvedFrom). This pin isolates
        // the OTHER arm of the helper's documented contract: "plus the latest
        // one whose rebalance date PRECEDES resolvedFrom so the simulation can
        // open with an initial portfolio."
        //
        // The risk this catches: a refactor that drops the
        // `if (lastBeforeWindow.HasValue && !relevant.Contains(lastBeforeWindow.Value))
        //     relevant.Insert(0, lastBeforeWindow.Value);`
        // tail — perhaps because the boundary test passes with or without it
        // when both snapshots are within the window — would compile, pass the
        // boundary pin, and on the first mid-quarter backtest start return an
        // empty list. The simulation would then open with no portfolio
        // because `ordered[snapshotIdx].Snapshot` (called inside
        // HoldingsBacktestCalculator.Calculate) needs at least one snapshot to
        // rebalance against, and an empty list crashes on `ordered[0]`. The
        // boundary test cannot catch this because its setup always produces
        // an in-window snapshot via `equalsFrom`.
        //
        // Construct three snapshots whose rebalance dates ALL precede the
        // window. Per the contract, the result must be a single-element list
        // containing the MOST RECENT of those snapshots — the simulation
        // opener. The two older snapshots are irrelevant: they would have
        // already rebalanced out before the simulation begins.
        var resolvedFrom = new DateOnly(2024, 6, 1);
        var resolvedTo = new DateOnly(2024, 12, 31);
        var rebalanceDelay = HoldingsBacktestCalculator.RebalanceDelayDays;

        // All rebalance dates precede resolvedFrom (2024-06-01). The dates
        // below produce rebalance dates 2023-08-14 / 2023-11-14 / 2024-02-14
        // respectively — all strictly less than 2024-06-01. The latest of
        // the three (mostRecent) has rebalance 2024-02-14.
        var oldest = new DateOnly(2023, 6, 30);
        var middle = new DateOnly(2023, 9, 30);
        var mostRecent = new DateOnly(2023, 12, 31);
        IReadOnlyList<DateOnly> reportDates = [oldest, middle, mostRecent];

        // Sanity-check the construction: all rebalance dates really do
        // precede resolvedFrom under whatever RebalanceDelayDays evaluates to.
        (mostRecent.AddDays(rebalanceDelay) < resolvedFrom).Should().BeTrue();

        var result =
            (List<DateOnly>)
                SelectRelevantSnapshotDatesMethod.Invoke(
                    null,
                    [reportDates, resolvedFrom, resolvedTo]
                );

        result.Should().ContainSingle();
        result[0].Should().Be(mostRecent);
    }
}
