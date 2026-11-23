using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

// Fiscal years as filed (prod Company Facts, revenue and total assets), placed in the calendar year holding most of
// their days, so "2025 revenue of NVDA, MSFT, AAPL, WMT and COST" lines up the same months.
public class CalendarYearAlignmentTests
{
    private static FinancialFact Fact(string start, string end, int stamp = 0, Guid? stock = null) =>
        new()
        {
            EquityIssuerId = stock ?? Guid.NewGuid(),
            PeriodStart = DateOnly.Parse(start),
            PeriodEnd = DateOnly.Parse(end),
            FiscalYear = stamp,
        };

    [Theory]
    [InlineData("2025-01-27", "2026-01-25", 2025)] // NVDA fiscal 2026: January year end
    [InlineData("2024-01-29", "2025-01-26", 2024)] // NVDA fiscal 2025
    [InlineData("2025-07-01", "2026-06-30", 2025)] // MSFT fiscal 2026: June year end, 184 days in 2025
    [InlineData("2024-07-01", "2025-06-30", 2024)] // MSFT fiscal 2025
    [InlineData("2024-09-29", "2025-09-27", 2025)] // AAPL fiscal 2025
    [InlineData("2025-02-01", "2026-01-31", 2025)] // WMT fiscal 2026
    [InlineData("2024-09-02", "2025-08-31", 2025)] // COST fiscal 2025: 52/53-week year
    [InlineData("2024-12-29", "2026-01-03", 2025)] // a 53-week year ending in early January
    public void CalendarYear_WholeFiscalYear_IsTheYearHoldingMostOfItsDays(
        string start,
        string end,
        int calendarYear
    ) => CalendarYearAlignment.CalendarYear(Fact(start, end)).Should().Be(calendarYear);

    [Theory]
    [InlineData("2026-01-25", 2025)] // NVDA's balance closes the year its fiscal 2026 revenue is placed in
    [InlineData("2026-06-30", 2025)] // MSFT
    [InlineData("2025-09-27", 2025)] // AAPL
    [InlineData("2026-01-31", 2025)] // WMT
    [InlineData("2025-12-31", 2025)] // a calendar-year filer
    public void CalendarYear_Balance_SitsWithTheYearItCloses(string date, int calendarYear) =>
        CalendarYearAlignment.CalendarYear(Fact(date, date)).Should().Be(calendarYear);

    [Theory]
    [InlineData("2025-01-01", "2025-09-30")] // nine months year to date
    [InlineData("2025-07-01", "2025-09-30")] // one quarter
    [InlineData("2023-01-01", "2025-12-31")] // three years
    public void CalendarYear_NotAWholeYear_IsNeverPlaced(string start, string end) =>
        CalendarYearAlignment.CalendarYear(Fact(start, end)).Should().BeNull();

    [Fact]
    public void ShareIn_JuneYearEnd_SplitsAboutEvenly()
    {
        CalendarYearAlignment
            .ShareIn(Fact("2025-07-01", "2026-06-30"), 2025)
            .Should()
            .BeLessThan(CalendarYearAlignment.EvenSplitShare);
        CalendarYearAlignment
            .ShareIn(Fact("2024-09-29", "2025-09-27"), 2025)
            .Should()
            .BeGreaterThan(CalendarYearAlignment.EvenSplitShare);
    }

    [Fact]
    public void Choose_PeersWithDifferentCalendars_TakesEachCompanysYearFallingMostlyInTheYear()
    {
        Guid nvda = Guid.NewGuid(), aapl = Guid.NewGuid(), amd = Guid.NewGuid();
        var endingIn2025 = new Dictionary<Guid, FinancialFact>
        {
            [nvda] = Fact("2024-01-29", "2025-01-26", 2025, nvda),
            [aapl] = Fact("2024-09-29", "2025-09-27", 2025, aapl),
            [amd] = Fact("2024-12-29", "2025-12-27", 2025, amd),
        };
        var endingIn2026 = new Dictionary<Guid, FinancialFact>
        {
            [nvda] = Fact("2025-01-27", "2026-01-25", 2026, nvda),
            [aapl] = Fact("2025-09-28", "2026-09-26", 2026, aapl),
        };

        var chosen = CalendarYearAlignment.Choose(2025, endingIn2025, endingIn2026);

        chosen[nvda].Fact.PeriodEnd.Should().Be(new DateOnly(2026, 1, 25));
        chosen[nvda].Stamp.Should().Be(2026);
        chosen[aapl].Fact.PeriodEnd.Should().Be(new DateOnly(2025, 9, 27));
        chosen[amd].Fact.PeriodEnd.Should().Be(new DateOnly(2025, 12, 27));
    }

    [Fact]
    public void Choose_YearNotYetFiled_HasNoRowRatherThanTheNeighbouringYear()
    {
        var nvda = Guid.NewGuid();
        var chosen = CalendarYearAlignment.Choose(
            2025,
            new Dictionary<Guid, FinancialFact> { [nvda] = Fact("2024-01-29", "2025-01-26", 2025, nvda) },
            new Dictionary<Guid, FinancialFact>()
        );

        chosen.Should().NotContainKey(nvda);
    }

    [Fact]
    public void Choose_SamePeriodUnderBothStamps_KeepsTheEarlierStamp()
    {
        var retailer = Guid.NewGuid();
        var chosen = CalendarYearAlignment.Choose(
            2025,
            new Dictionary<Guid, FinancialFact> { [retailer] = Fact("2024-12-29", "2026-01-03", 2025, retailer) },
            new Dictionary<Guid, FinancialFact> { [retailer] = Fact("2024-12-29", "2026-01-03", 2026, retailer) }
        );

        chosen[retailer].Stamp.Should().Be(2025);
    }

    [Fact]
    public void Choose_ChangedYearEnd_KeepsThePeriodWithMoreDaysInTheYear()
    {
        // A filer moving its year end from September to December: the old year ending September 2025 and the new
        // one ending December 2025 both fall mostly in 2025; the new year holds all of its days there.
        var filer = Guid.NewGuid();
        var chosen = CalendarYearAlignment.Choose(
            2025,
            new Dictionary<Guid, FinancialFact>
            {
                [filer] = Fact("2024-10-01", "2025-09-30", 2025, filer),
            },
            new Dictionary<Guid, FinancialFact>
            {
                [filer] = Fact("2025-01-01", "2025-12-31", 2026, filer),
            }
        );

        chosen[filer].Fact.PeriodEnd.Should().Be(new DateOnly(2025, 12, 31));
    }
}
