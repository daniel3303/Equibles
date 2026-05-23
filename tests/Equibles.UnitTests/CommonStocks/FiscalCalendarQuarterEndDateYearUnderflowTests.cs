using Equibles.CommonStocks.BusinessLogic;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Contract: GetQuarterEndDate accepts any (fiscalYear, fiscalQuarter,
/// fiscalYearEndMonth) triple and returns a valid DateOnly. For off-calendar
/// filers the quarter-end walks backwards from the FYE month, and for Q1 of
/// fiscal year 1 with a non-December FYE, that walk reaches calendar year 0
/// — which is outside DateOnly's valid range (year 1–9999). The method should
/// either reject the input or clamp gracefully, not throw an unhandled
/// ArgumentOutOfRangeException from the DateOnly constructor.
/// </summary>
public class FiscalCalendarQuarterEndDateYearUnderflowTests
{
    [Fact]
    public void GetQuarterEndDate_FiscalYear1MarchFyeQ1_ThrowsArgumentOutOfRange()
    {
        // March FYE: Q1 ends in June of the PREVIOUS calendar year (fiscalYear - 1 = 0).
        var act = () => FiscalCalendar.GetQuarterEndDate(1, 1, 3);

        act.Should().ThrowExactly<ArgumentOutOfRangeException>().WithParameterName("fiscalYear");
    }
}
