using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// The EDGAR header's periodOfReport is the period SEC publishes for a 13F; the cover page's
// reportCalendarOrQuarter is typed by the filer and sometimes disagrees with it.
public class Filing13FXmlParserHeaderPeriodOfReportTests
{
    private readonly Filing13FXmlParser _sut = new();

    [Fact]
    public void ParseCoverPage_CoverPageQuarterDisagreesWithHeader_UsesHeaderPeriod()
    {
        // A real shape: the filer typed its signature date into the cover page's
        // "report for the calendar year or quarter ended" field.
        var result = _sut.ParseCoverPage(
            Filing(headerPeriod: "06-30-2020", coverQuarter: "08-14-2020"),
            accessionNumber: "0000000001-20-000001",
            cik: "0000000001",
            filingDate: new DateOnly(2020, 8, 14)
        );

        result.PeriodOfReport.Should().Be(new DateOnly(2020, 6, 30));
    }

    [Fact]
    public void ParseCoverPage_CoverPageNamesThePreviousQuarter_UsesHeaderPeriod()
    {
        // The other real shape: a valid quarter end, just the wrong one, which would
        // otherwise overwrite the filer's genuine previous-quarter book.
        var result = _sut.ParseCoverPage(
            Filing(headerPeriod: "06-30-2020", coverQuarter: "03-31-2020"),
            accessionNumber: "0000000001-20-000002",
            cik: "0000000001",
            filingDate: new DateOnly(2020, 8, 7)
        );

        result.PeriodOfReport.Should().Be(new DateOnly(2020, 6, 30));
    }

    [Fact]
    public void ParseCoverPage_HeaderPeriodMissing_FallsBackToCoverPageQuarter()
    {
        var result = _sut.ParseCoverPage(
            Filing(headerPeriod: null, coverQuarter: "03-31-2026"),
            accessionNumber: "0000000001-26-000001",
            cik: "0000000001",
            filingDate: new DateOnly(2026, 5, 15)
        );

        result.PeriodOfReport.Should().Be(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public void ParseCoverPage_HeaderPeriodUnparseable_FallsBackToCoverPageQuarter()
    {
        var result = _sut.ParseCoverPage(
            Filing(headerPeriod: "Q1 2026", coverQuarter: "03-31-2026"),
            accessionNumber: "0000000001-26-000002",
            cik: "0000000001",
            filingDate: new DateOnly(2026, 5, 15)
        );

        result.PeriodOfReport.Should().Be(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public void ParseCoverPage_BothPeriodsMissing_LeavesPeriodUnset()
    {
        var result = _sut.ParseCoverPage(
            Filing(headerPeriod: null, coverQuarter: null),
            accessionNumber: "0000000001-26-000003",
            cik: "0000000001",
            filingDate: new DateOnly(2026, 5, 15)
        );

        result.PeriodOfReport.Should().Be(DateOnly.MinValue);
    }

    // Mirrors EDGAR's primary_doc.xml layout: the period lives under
    // headerData/filerInfo, the cover page under formData.
    private static string Filing(string headerPeriod, string coverQuarter)
    {
        var header = headerPeriod == null ? "" : $"<periodOfReport>{headerPeriod}</periodOfReport>";
        var cover =
            coverQuarter == null
                ? ""
                : $"<reportCalendarOrQuarter>{coverQuarter}</reportCalendarOrQuarter>";
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<edgarSubmission xmlns=\"http://www.sec.gov/edgar/thirteenffiler\">"
            + "  <headerData>"
            + "    <submissionType>13F-HR</submissionType>"
            + "    <filerInfo>"
            + "      <filer><credentials><cik>0000000001</cik></credentials></filer>"
            + $"      {header}"
            + "    </filerInfo>"
            + "  </headerData>"
            + "  <formData>"
            + "    <coverPage>"
            + $"      {cover}"
            + "      <isAmendment>false</isAmendment>"
            + "      <filingManager><name>Example Manager</name></filingManager>"
            + "    </coverPage>"
            + "  </formData>"
            + "</edgarSubmission>";
    }
}
