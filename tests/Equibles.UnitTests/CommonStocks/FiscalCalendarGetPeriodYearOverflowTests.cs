using Equibles.CommonStocks.BusinessLogic;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Contract: GetPeriod computes fiscalYear = date.Year + 1 when the date falls
/// after the FYE month. For DateOnly.MaxValue (Dec 31, 9999) with a non-December
/// FYE, this produces fiscal year 10000 — outside DateOnly's supported range
/// (1–9999) and impossible to round-trip through GetQuarterEndDate, which guards
/// against the same overflow. GetPeriod should reject the overflow consistently.
/// </summary>
public class FiscalCalendarGetPeriodYearOverflowTests
{
    [Fact(Skip = "GH-1884 — GetPeriod year overflow to fiscal year 10000")]
    public void GetPeriod_MaxValueDateWithNonDecemberFye_ThrowsArgumentOutOfRange()
    {
        // Dec 31, 9999 with June FYE: fiscalYear = 9999 + 1 = 10000.
        var act = () => FiscalCalendar.GetPeriod(DateOnly.MaxValue, fiscalYearEndMonth: 6);

        act.Should().ThrowExactly<ArgumentOutOfRangeException>().WithParameterName("date");
    }
}
