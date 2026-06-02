using System.Reflection;
using Equibles.Holdings.BusinessLogic;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Contract (method summary): SelectRelevantSnapshotDates returns "all snapshots whose rebalance
/// date falls in [from, to], plus the latest one whose rebalance precedes `from` so the simulation
/// can open with an already-held portfolio." When several snapshots fall before the window, only
/// the most recent of them carries the opening portfolio — the earlier ones are dropped — and it
/// must lead the chronological list so the backtest starts already invested.
/// </summary>
public class FundScoringManagerSelectRelevantSnapshotDatesLatestBeforeWindowTests
{
    [Fact]
    public void SelectRelevantSnapshotDates_MultiplePreWindowSnapshots_CarriesOnlyLatestAheadOfInWindow()
    {
        var method = typeof(FundScoringManager).GetMethod(
            "SelectRelevantSnapshotDates",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var from = new DateOnly(2020, 1, 1);
        var to = new DateOnly(2023, 1, 1);
        // Ascending, as the caller passes them. The first two rebalance (+45d) before `from`;
        // only the later (2019-06-01) should open the portfolio. 2021-06-01 lands in-window.
        IReadOnlyList<DateOnly> reportDates =
        [
            new DateOnly(2018, 6, 1),
            new DateOnly(2019, 6, 1),
            new DateOnly(2021, 6, 1),
        ];

        var result = (List<DateOnly>)method.Invoke(null, [reportDates, from, to]);

        result.Should().Equal(new DateOnly(2019, 6, 1), new DateOnly(2021, 6, 1));
    }
}
