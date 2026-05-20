using System.Reflection;
using Equibles.Holdings.Repositories;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class HoldingsBacktestServiceSelectRelevantSnapshotsBoundaryTests
{
    private static readonly MethodInfo SelectRelevantSnapshotDatesMethod =
        typeof(HoldingsBacktestService).GetMethod(
            "SelectRelevantSnapshotDates",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // The newly-extracted SelectRelevantSnapshotDates helper (#1444) documents:
    // "pick all snapshots whose rebalance date falls in [resolvedFrom,
    // resolvedTo], plus the latest one whose rebalance date PRECEDES
    // resolvedFrom so the simulation can open with an initial portfolio." The
    // word "precedes" is strict-less; rebalance == resolvedFrom is therefore
    // in-window, not the opening pre-window snapshot. A single-character
    // refactor flipping the first arm from `rebalance < resolvedFrom` to
    // `<= resolvedFrom` would silently send the equals-from snapshot down the
    // `lastBeforeWindow` path, overwriting the genuine pre-window opener and
    // collapsing two distinct snapshots into one — the simulation would lose
    // a rebalance signal at the window's leading edge.
    [Fact]
    public void SelectRelevantSnapshotDates_RebalanceExactlyEqualsResolvedFrom_TreatedAsInWindowNotOpener()
    {
        // Construct so rebalance dates straddle the boundary: 45-day delay
        // (HoldingsBacktestCalculator.RebalanceDelayDays) means a reportDate of
        // resolvedFrom - 45 has rebalance == resolvedFrom (the equals-from
        // case), while resolvedFrom - 46 sits one day inside the pre-window
        // region — the genuine opener.
        var resolvedFrom = new DateOnly(2024, 3, 15);
        var resolvedTo = new DateOnly(2024, 12, 31);
        var preWindowOpener = resolvedFrom.AddDays(-46);
        var equalsFrom = resolvedFrom.AddDays(-HoldingsBacktestCalculator.RebalanceDelayDays);
        IReadOnlyList<DateOnly> reportDates = [preWindowOpener, equalsFrom];

        var result =
            (List<DateOnly>)
                SelectRelevantSnapshotDatesMethod.Invoke(
                    null,
                    [reportDates, resolvedFrom, resolvedTo]
                );

        result.Should().Equal(preWindowOpener, equalsFrom);
    }
}
