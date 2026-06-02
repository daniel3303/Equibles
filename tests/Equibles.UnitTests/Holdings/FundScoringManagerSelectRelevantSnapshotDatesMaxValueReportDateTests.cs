using System.Reflection;
using Equibles.Holdings.BusinessLogic;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Contract (class summary): FundScoringManager "mirror[s] the on-demand backtest in the web
/// portal's HoldingsBacktestService". Its sibling there — and SmartMoneyIndexManager in this same
/// module — both clamp the +45-day rebalance shift at DateOnly.MaxValue precisely because a
/// ReportDate within RebalanceDelayDays of the calendar's end would overflow AddDays. ScoreHolder
/// also promises to "return ... null when there isn't enough data to simulate", i.e. graceful
/// degradation on bad inputs, never a thrown exception.
///
/// SelectRelevantSnapshotDates here calls reportDate.AddDays(45) with no such clamp, so a
/// far-future ReportDate throws ArgumentOutOfRangeException instead of being treated like any
/// other out-of-window snapshot. The clamped sibling resolves MaxValue's rebalance to MaxValue,
/// which sits after a normal window's upper bound and is simply excluded — so the expected result
/// is the in-window snapshot alone, with no throw.
/// </summary>
public class FundScoringManagerSelectRelevantSnapshotDatesMaxValueReportDateTests
{
    [Fact(Skip = "GH-3221 — FundScoringManager.SelectRelevantSnapshotDates throws on a MaxValue ReportDate")]
    public void SelectRelevantSnapshotDates_ReportDateAtMaxValue_ExcludesItWithoutThrowing()
    {
        var method = typeof(FundScoringManager).GetMethod(
            "SelectRelevantSnapshotDates",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var from = new DateOnly(2020, 1, 1);
        var to = new DateOnly(2023, 1, 1);
        // Ascending, as the caller passes them. The far-future date's rebalance lands beyond `to`.
        IReadOnlyList<DateOnly> reportDates = [new DateOnly(2021, 6, 1), DateOnly.MaxValue];

        var result = (List<DateOnly>)method.Invoke(null, [reportDates, from, to]);

        result.Should().ContainSingle().Which.Should().Be(new DateOnly(2021, 6, 1));
    }
}
