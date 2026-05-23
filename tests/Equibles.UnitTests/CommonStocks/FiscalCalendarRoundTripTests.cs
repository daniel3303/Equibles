using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Contract from the GetQuarterEndDate doc comment: "Round-trips with GetPeriod:
/// GetPeriod(GetQuarterEndDate(y, q, m), m) == new FiscalPeriod(y, q)."
/// This property must hold for every valid (year, quarter, month) triple —
/// failure means the two methods disagree on period boundaries, which would
/// silently mislabel financial data for off-calendar filers.
/// </summary>
public class FiscalCalendarRoundTripTests
{
    [Fact]
    public void GetPeriodOfQuarterEndDate_AllMonthsAllQuarters_RoundTrips()
    {
        const int testYear = 2025;
        for (var month = 1; month <= 12; month++)
        {
            for (var quarter = 1; quarter <= 4; quarter++)
            {
                var endDate = FiscalCalendar.GetQuarterEndDate(testYear, quarter, month);
                var roundTripped = FiscalCalendar.GetPeriod(endDate, month);

                roundTripped
                    .Should()
                    .Be(
                        new FiscalPeriod(testYear, quarter),
                        $"month={month} quarter={quarter} → endDate={endDate}"
                    );
            }
        }
    }
}
