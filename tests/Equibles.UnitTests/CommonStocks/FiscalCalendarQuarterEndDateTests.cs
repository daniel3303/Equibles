using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Pins the inverse fiscal mapping (period → quarter-end date). Earnings-call
/// ingestion gets a fiscal (year, quarter) from the data provider and must
/// store the *real* period-end — storing a calendar quarter-end for an
/// off-calendar filer (Apple's FQ1 ends in December, not March) is exactly the
/// historical-data bug this helper exists to prevent.
/// </summary>
public class FiscalCalendarQuarterEndDateTests
{
    [Theory]
    // Apple — fiscal year ends in September.
    [InlineData(2024, 1, 9, "2023-12-31")]
    [InlineData(2024, 2, 9, "2024-03-31")]
    [InlineData(2024, 3, 9, "2024-06-30")]
    [InlineData(2024, 4, 9, "2024-09-30")]
    // Microsoft — fiscal year ends in June.
    [InlineData(2024, 1, 6, "2023-09-30")]
    [InlineData(2024, 2, 6, "2023-12-31")]
    [InlineData(2024, 3, 6, "2024-03-31")]
    [InlineData(2024, 4, 6, "2024-06-30")]
    // Plain calendar-year filer.
    [InlineData(2024, 1, 12, "2024-03-31")]
    [InlineData(2024, 4, 12, "2024-12-31")]
    // Fiscal year ending February — Q4 end lands on a leap day.
    [InlineData(2024, 4, 2, "2024-02-29")]
    [InlineData(2023, 4, 2, "2023-02-28")]
    public void GetQuarterEndDate_ReturnsLastDayOfTheFiscalQuarter(
        int fiscalYear,
        int fiscalQuarter,
        int fiscalYearEndMonth,
        string expected
    )
    {
        var result = FiscalCalendar.GetQuarterEndDate(
            fiscalYear,
            fiscalQuarter,
            fiscalYearEndMonth
        );

        result.Should().Be(DateOnly.Parse(expected));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(9)]
    [InlineData(12)]
    public void GetQuarterEndDate_RoundTripsWithGetPeriod(int fiscalYearEndMonth)
    {
        for (var year = 2020; year <= 2026; year++)
        {
            for (var quarter = 1; quarter <= 4; quarter++)
            {
                var end = FiscalCalendar.GetQuarterEndDate(year, quarter, fiscalYearEndMonth);

                FiscalCalendar
                    .GetPeriod(end, fiscalYearEndMonth)
                    .Should()
                    .Be(
                        new FiscalPeriod(year, quarter),
                        "the quarter-end date must map back to the same fiscal period"
                    );
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void GetQuarterEndDate_InvalidQuarter_Throws(int fiscalQuarter)
    {
        var act = () => FiscalCalendar.GetQuarterEndDate(2024, fiscalQuarter, 9);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void GetQuarterEndDate_InvalidFiscalYearEndMonth_Throws(int fiscalYearEndMonth)
    {
        var act = () => FiscalCalendar.GetQuarterEndDate(2024, 1, fiscalYearEndMonth);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetQuarterEndDate_StockWithoutDetectedFiscalYearEnd_ReturnsNull()
    {
        var stock = new CommonStock { FiscalYearEndMonth = null };

        FiscalCalendar.GetQuarterEndDate(2024, 1, stock).Should().BeNull();
    }

    [Fact]
    public void GetQuarterEndDate_StockWithDetectedFiscalYearEnd_ReturnsDate()
    {
        var stock = new CommonStock { FiscalYearEndMonth = 9 };

        FiscalCalendar.GetQuarterEndDate(2024, 1, stock).Should().Be(new DateOnly(2023, 12, 31));
    }

    [Fact]
    public void GetQuarterEndDate_NullStock_Throws()
    {
        var act = () => FiscalCalendar.GetQuarterEndDate(2024, 1, (CommonStock)null);

        act.Should().Throw<ArgumentNullException>();
    }
}
