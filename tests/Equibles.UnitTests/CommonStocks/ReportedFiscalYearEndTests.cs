using Equibles.CommonStocks.BusinessLogic;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Each case is a registrant's EDGAR company record and report period ends as listed on
/// 2026-11-14.
/// </summary>
public class ReportedFiscalYearEndTests
{
    private static readonly DateOnly Today = new(2026, 11, 14);

    private static (int Month, int? Day)? Choose(
        string record,
        string[] annualEnds,
        string[] quarterEnds,
        bool hasRecordedYearEnd = true,
        bool filesAnnualReportsNow = true,
        string recorded = null
    )
    {
        // Unless a case says otherwise, what is recorded so far came from the company record.
        recorded ??= hasRecordedYearEnd ? record : null;
        return ReportedFiscalYearEnd.Choose(
            record == null ? null : int.Parse(record[..2]),
            record == null ? null : int.Parse(record[2..]),
            annualEnds.Select(DateOnly.Parse).ToList(),
            quarterEnds.Select(DateOnly.Parse).ToList(),
            recorded == null ? null : int.Parse(recorded[..2]),
            recorded == null ? null : int.Parse(recorded[2..]),
            filesAnnualReportsNow,
            Today
        );
    }

    [Theory]
    // Brown & Brown: recorded January 19, every annual report closes December 31.
    [InlineData("0119", "2025-12-31,2024-12-31", "2026-09-30,2026-06-30,2026-03-31", 12, 31)]
    // LifeVantage: recorded December 31, the year closes June 30.
    [InlineData("1231", "2026-06-30,2025-06-30", "2026-09-30,2026-03-31,2025-12-31", 6, 30)]
    // Culp: a 52/53-week year closing around the end of April, recorded December 31.
    [InlineData(
        "1231",
        "2026-05-03,2025-04-27",
        "2026-08-02,2026-02-01,2025-11-02,2025-08-03",
        4,
        30
    )]
    // Sun Communities: recorded May 13.
    [InlineData("0513", "2025-12-31,2024-12-31", "2026-09-30,2026-06-30,2026-03-31", 12, 31)]
    public void Choose_AgreeingCurrentAnnualReports_OutrankTheCompanyRecord(
        string record,
        string annualEnds,
        string quarterEnds,
        int month,
        int day
    )
    {
        Choose(record, annualEnds.Split(','), quarterEnds.Split(','))
            .Should()
            .Be(((int, int?))(month, day));
    }

    [Theory]
    // Greif: a transition report closed September 30 after October years.
    [InlineData(
        "0930",
        "2025-09-30,2024-10-31",
        "2026-06-30,2026-03-31,2025-12-31,2025-07-31",
        9,
        30
    )]
    // Ferguson: a transition report closed December 31 after July years.
    [InlineData(
        "1231",
        "2025-12-31,2025-07-31",
        "2026-09-30,2026-06-30,2026-03-31,2025-10-31",
        12,
        31
    )]
    // Aehr: May years, then a quarter closing in September ahead of any transition report.
    [InlineData(
        "1231",
        "2026-05-29,2025-05-30",
        "2026-09-25,2026-02-27,2025-11-28,2025-08-29",
        12,
        31
    )]
    public void Choose_ReportsShowingAChangeOfYearEnd_KeepTheCompanyRecord(
        string record,
        string annualEnds,
        string quarterEnds,
        int month,
        int day
    )
    {
        Choose(record, annualEnds.Split(','), quarterEnds.Split(','))
            .Should()
            .Be(((int, int?))(month, day));
    }

    [Theory]
    // Apple and Costco: the record's month is the reports' month, so the record stands.
    [InlineData("0926", "2026-09-26,2025-09-27", "2026-06-27,2026-03-28,2025-12-27", 9, 26)]
    [InlineData("0830", "2026-08-30,2025-08-31", "2026-05-10,2026-02-15,2025-11-23", 8, 30)]
    // A year closing on the Saturday nearest January 31 is recorded as February 1 some years.
    [InlineData("0201", "2026-01-31,2025-02-01", "2026-08-01,2026-05-02,2025-11-01", 1, 31)]
    // A year closing on the Saturday nearest December 31 keeps its early-January record.
    [InlineData("0103", "2026-01-03,2024-12-28", "2026-10-03,2026-07-04,2026-04-04", 1, 3)]
    // Twelve-week quarters close off the month grid every year, which is no change of year end.
    [InlineData(
        "1227",
        "2025-12-27,2024-12-28",
        "2026-09-05,2026-06-13,2026-03-21,2025-09-06",
        12,
        27
    )]
    public void Choose_RecordAgreeingWithTheReports_StoresTheRecord(
        string record,
        string annualEnds,
        string quarterEnds,
        int month,
        int day
    )
    {
        Choose(record, annualEnds.Split(','), quarterEnds.Split(','))
            .Should()
            .Be(((int, int?))(month, day));
    }

    [Fact]
    public void Choose_AgreeingReportsNoLongerCurrent_LeaveTheRecordedYearEndAlone()
    {
        // FuboTV: December years through 2024, no annual report since, record says September 30.
        var chosen = Choose(
            "0930",
            ["2024-12-31", "2023-12-31"],
            ["2026-06-30", "2026-03-31", "2025-12-31", "2025-09-30"]
        );

        chosen.Should().BeNull();
    }

    [Theory]
    // A registrant that files heavily lists one annual report for part of the year.
    [InlineData("2025-12-31", "2026-09-30,2026-06-30,2026-03-31")]
    // The year before's report is missing from the list, so the two listed are two years apart.
    [InlineData("2025-12-31,2023-12-31", "2026-09-30,2026-06-30,2026-03-31")]
    public void Choose_ReportsNoLongerEnoughToOutrankTheRecord_KeepTheYearEndTheySetEarlier(
        string annualEnds,
        string quarterEnds
    )
    {
        Choose("0119", annualEnds.Split(','), quarterEnds.Split(','), recorded: "1231")
            .Should()
            .BeNull();
    }

    [Fact]
    public void Choose_NewestAnnualReportOnTheRecordsMonth_TakesTheRecordOverTheRecordedYearEnd()
    {
        // A real change of year end: July years, then a transition report closing December 31.
        Choose(
                "1231",
                ["2025-12-31", "2025-07-31"],
                ["2026-09-30", "2026-06-30", "2026-03-31"],
                recorded: "0731"
            )
            .Should()
            .Be(((int, int?))(12, 31));
    }

    [Fact]
    public void Choose_QuarterOffTheRecordedCalendar_TakesTheRecordBeforeAnAnnualReportConfirmsIt()
    {
        // May years so far, then a quarter closing in late September: the record's December
        // year end is the only statement of the new calendar until an annual report closes on it.
        Choose(
                "1231",
                ["2026-05-29", "2025-05-30"],
                ["2026-09-25", "2026-02-27", "2025-11-28", "2025-08-29"],
                recorded: "0531"
            )
            .Should()
            .Be(((int, int?))(12, 31));
    }

    [Fact]
    public void Choose_FilerWithoutACurrentAnnualReport_OnlyFillsABlank()
    {
        string[] none = [];

        Choose("0228", none, none, hasRecordedYearEnd: true, filesAnnualReportsNow: false)
            .Should()
            .BeNull();
        Choose("0228", none, none, hasRecordedYearEnd: false, filesAnnualReportsNow: false)
            .Should()
            .Be(((int, int?))(2, 28));
    }
}
