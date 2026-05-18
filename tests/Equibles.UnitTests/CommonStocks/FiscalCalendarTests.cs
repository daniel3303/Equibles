using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Pins the fiscal-quarter contract for off-calendar filers. The fiscal year
/// is labelled by the calendar year it ends in (US filer convention), so
/// Apple's October-2023 quarter is FY2024 Q1, not FY2023 Q4 — getting this
/// wrong would mislabel every quarter for ~40% of S&P 500 filers.
/// </summary>
public class FiscalCalendarTests
{
    [Theory]
    // Apple — fiscal year ends in September.
    [InlineData(9, "2023-10-01", 2024, 1)]
    [InlineData(9, "2023-12-31", 2024, 1)]
    [InlineData(9, "2024-01-01", 2024, 2)]
    [InlineData(9, "2024-06-15", 2024, 3)]
    [InlineData(9, "2024-09-28", 2024, 4)]
    [InlineData(9, "2024-10-01", 2025, 1)]
    // Microsoft — fiscal year ends in June.
    [InlineData(6, "2023-07-01", 2024, 1)]
    [InlineData(6, "2024-06-30", 2024, 4)]
    [InlineData(6, "2024-07-01", 2025, 1)]
    // Plain calendar-year filer.
    [InlineData(12, "2024-01-15", 2024, 1)]
    [InlineData(12, "2024-04-01", 2024, 2)]
    [InlineData(12, "2024-12-31", 2024, 4)]
    public void GetPeriod_MapsDateToFiscalQuarterAndYear(
        int fiscalYearEndMonth,
        string date,
        int expectedYear,
        int expectedQuarter
    )
    {
        var period = FiscalCalendar.GetPeriod(DateOnly.Parse(date), fiscalYearEndMonth);

        period.Year.Should().Be(expectedYear);
        period.Quarter.Should().Be(expectedQuarter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void GetPeriod_InvalidFiscalYearEndMonth_Throws(int fiscalYearEndMonth)
    {
        var act = () => FiscalCalendar.GetPeriod(new DateOnly(2024, 1, 1), fiscalYearEndMonth);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetPeriod_StockWithoutDetectedFiscalYearEnd_ReturnsNull()
    {
        var stock = new CommonStock { FiscalYearEndMonth = null };

        var period = FiscalCalendar.GetPeriod(new DateOnly(2024, 1, 1), stock);

        period.Should().BeNull();
    }

    [Fact]
    public void GetPeriod_StockWithDetectedFiscalYearEnd_ReturnsPeriod()
    {
        var stock = new CommonStock { FiscalYearEndMonth = 9 };

        var period = FiscalCalendar.GetPeriod(new DateOnly(2023, 10, 1), stock);

        period.Should().Be(new FiscalPeriod(2024, 1));
    }

    [Fact]
    public void GetPeriod_NullStock_Throws()
    {
        var act = () => FiscalCalendar.GetPeriod(new DateOnly(2024, 1, 1), (CommonStock)null);

        act.Should().Throw<ArgumentNullException>();
    }
}
