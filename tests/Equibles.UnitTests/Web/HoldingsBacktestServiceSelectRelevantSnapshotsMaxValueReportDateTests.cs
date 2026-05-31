using System.Reflection;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

/// <summary>
/// Per-snapshot sibling to the rebalance-date overflow guard. SelectRelevantSnapshotDates
/// shifts each ReportDate forward to its rebalance date (+45 days); a ReportDate within
/// RebalanceDelayDays of DateOnly.MaxValue would overflow the calendar with an unguarded
/// AddDays. The contract is that any stored snapshot must be classified gracefully — never
/// throw — so the page can render a "no data" result. With a normal requested window, the
/// clamped rebalance date (MaxValue) sits past resolvedTo and the snapshot is simply dropped.
/// </summary>
public class HoldingsBacktestServiceSelectRelevantSnapshotsMaxValueReportDateTests
{
    private static readonly MethodInfo SelectRelevantSnapshotDatesMethod =
        typeof(HoldingsBacktestService).GetMethod(
            "SelectRelevantSnapshotDates",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    [Fact]
    public void SelectRelevantSnapshotDates_ReportDateAtMaxValue_DoesNotThrowAndDropsOutOfWindowSnapshot()
    {
        var resolvedFrom = new DateOnly(2024, 1, 1);
        var resolvedTo = new DateOnly(2024, 12, 31);
        IReadOnlyList<DateOnly> reportDates = [DateOnly.MaxValue];

        var result =
            (List<DateOnly>)
                SelectRelevantSnapshotDatesMethod.Invoke(
                    null,
                    [reportDates, resolvedFrom, resolvedTo]
                );

        // Rebalance clamps to MaxValue, which is after resolvedTo: out of window, not an
        // opener — so nothing is selected, and no overflow is thrown along the way.
        result.Should().BeEmpty();
    }
}
