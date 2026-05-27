using System.Reflection;
using Equibles.Holdings.Repositories;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

/// <summary>
/// Sibling to <c>SelectRelevantSnapshotsBoundaryTests</c> (leading edge,
/// <c>rebalance == resolvedFrom</c> → in-window) and to
/// <c>SelectRelevantSnapshotsPostWindowTests</c>
/// (<c>rebalance &gt; resolvedTo</c> → dropped). The trailing edge — when
/// <c>rebalance == resolvedTo</c> exactly — has no dedicated pin and is the
/// symmetric off-by-one risk. The docstring says "rebalance date falls in
/// [resolvedFrom, resolvedTo]" — the right bracket is inclusive, mirroring
/// the left. A refactor flipping <c>else if (rebalance &lt;= resolvedTo)</c> to
/// <c>&lt; resolvedTo</c> would silently drop the boundary snapshot — losing
/// the rebalance signal at the simulation window's trailing edge while
/// passing every existing pin.
/// </summary>
public class HoldingsBacktestServiceSelectRelevantSnapshotsTrailingBoundaryTests
{
    private static readonly MethodInfo SelectRelevantSnapshotDatesMethod =
        typeof(HoldingsBacktestService).GetMethod(
            "SelectRelevantSnapshotDates",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    [Fact]
    public void SelectRelevantSnapshotDates_RebalanceExactlyEqualsResolvedTo_TreatedAsInWindow()
    {
        var resolvedFrom = new DateOnly(2024, 3, 15);
        var resolvedTo = new DateOnly(2024, 12, 31);
        var rebalanceDelay = HoldingsBacktestCalculator.RebalanceDelayDays;
        // reportDate such that rebalance = resolvedTo exactly.
        var equalsTo = resolvedTo.AddDays(-rebalanceDelay);

        equalsTo.AddDays(rebalanceDelay).Should().Be(resolvedTo);

        IReadOnlyList<DateOnly> reportDates = [equalsTo];

        var result =
            (List<DateOnly>)
                SelectRelevantSnapshotDatesMethod.Invoke(
                    null,
                    [reportDates, resolvedFrom, resolvedTo]
                );

        result.Should().Equal(equalsTo);
    }
}
