using Equibles.Sec.FinancialFacts.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class FiscalYearNamingTests
{
    private static AnnualReportFiscalYear Report(
        string periodEnd,
        int secFiscalYear,
        int storedFiscalYear,
        string accession = null,
        string filed = null
    )
    {
        var end = DateOnly.Parse(periodEnd, System.Globalization.CultureInfo.InvariantCulture);
        return new AnnualReportFiscalYear(
            accession ?? $"0000000000-{storedFiscalYear % 100:00}-000001",
            filed == null
                ? end.AddDays(45)
                : DateOnly.Parse(filed, System.Globalization.CultureInfo.InvariantCulture),
            end,
            secFiscalYear,
            storedFiscalYear
        );
    }

    [Fact]
    public void Read_YearNamedForTheCalendarYearItEndsIn_IsZero()
    {
        // NVIDIA: the year ended 2026-01-25 is its fiscal 2026.
        FiscalYearNaming
            .Read([
                Report("2026-01-25", 2026, 2026),
                Report("2025-01-26", 2025, 2025),
                Report("2024-01-28", 2024, 2024),
            ])
            .Should()
            .Be(0);
    }

    [Fact]
    public void Read_YearNamedForTheCalendarYearItStartsIn_IsMinusOne()
    {
        // Home Depot: the year ended 2026-02-01 is its fiscal 2025.
        FiscalYearNaming
            .Read([
                Report("2026-02-01", 2025, 2026),
                Report("2025-02-02", 2024, 2025),
                Report("2024-01-28", 2023, 2024),
            ])
            .Should()
            .Be(-1);
    }

    [Theory]
    // One report mistyped: Salesforce's newest carries last year's number.
    [InlineData(2025, 2025, 2024)]
    // A start-year retailer whose newest report carries the ends-in year.
    [InlineData(2026, 2024, 2023)]
    // The mistyped report is the oldest of the three read.
    [InlineData(2025, 2024, 2024)]
    public void Read_ReportsThatDisagree_SettleNothing(int newest, int middle, int oldest)
    {
        FiscalYearNaming
            .Read([
                Report("2026-01-31", newest, 2026),
                Report("2025-01-31", middle, 2025),
                Report("2024-01-31", oldest, 2024),
            ])
            .Should()
            .BeNull();
    }

    [Fact]
    public void Read_DisagreementOlderThanTheReportsRead_IsIgnored()
    {
        FiscalYearNaming
            .Read([
                Report("2026-02-01", 2025, 2026),
                Report("2025-02-02", 2024, 2025),
                Report("2024-01-28", 2023, 2024),
                Report("2023-01-29", 2023, 2023),
            ])
            .Should()
            .Be(-1);
    }

    [Fact]
    public void Read_OneReportOnly_GivesItsAnswer()
    {
        FiscalYearNaming.Read([Report("2026-02-01", 2025, 2026)]).Should().Be(-1);
    }

    [Fact]
    public void Read_NoReport_SettlesNothing()
    {
        FiscalYearNaming.Read([]).Should().BeNull();
    }

    [Theory]
    // A lower year on a June or December year end is a typing error, never a naming.
    [InlineData("2026-06-30", "2025-06-30")]
    [InlineData("2025-12-31", "2024-12-31")]
    // A 52/53-week December year closing in January's first week is still a December year.
    [InlineData("2026-01-03", "2024-12-28")]
    public void Read_LowerYearOnALaterYearEnd_SettlesNothing(string newestEnd, string olderEnd)
    {
        var newest = DateOnly.Parse(newestEnd, System.Globalization.CultureInfo.InvariantCulture);
        var older = DateOnly.Parse(olderEnd, System.Globalization.CultureInfo.InvariantCulture);
        var newestStored = newest.Day <= 7 && newest.Month == 1 ? newest.Year - 1 : newest.Year;

        FiscalYearNaming
            .Read([
                Report(newestEnd, newestStored - 1, newestStored),
                Report(olderEnd, older.Year - 1, older.Year),
            ])
            .Should()
            .BeNull();
    }

    [Fact]
    public void Read_MarchYearClosingInAprilsFirstWeek_CanBeNamedForItsStart()
    {
        FiscalYearNaming
            .Read([Report("2026-04-03", 2025, 2026), Report("2025-03-28", 2024, 2025)])
            .Should()
            .Be(-1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-2)]
    public void Read_YearFurtherFromTheStoredOne_SettlesNothing(int offset)
    {
        FiscalYearNaming
            .Read([
                Report("2026-01-31", 2026 + offset, 2026),
                Report("2025-01-31", 2025 + offset, 2025),
            ])
            .Should()
            .BeNull();
    }

    [Fact]
    public void Read_YearFiledTwice_CountsOnceByItsNewestFiling()
    {
        // The first filing of fiscal 2026 carried the ends-in year; the company filed the year
        // again with its own number, and that later filing is the one read.
        FiscalYearNaming
            .Read([
                Report("2026-02-01", 2026, 2026, "0000000000-26-000001", "2026-03-18"),
                Report("2026-02-01", 2025, 2026, "0000000000-26-000009", "2026-04-02"),
                Report("2025-02-02", 2024, 2025),
            ])
            .Should()
            .Be(-1);
    }
}
