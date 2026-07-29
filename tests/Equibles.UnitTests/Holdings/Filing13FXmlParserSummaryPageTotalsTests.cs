using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Filing13FXmlParserSummaryPageTotalsTests
{
    private static readonly Filing13FXmlParser Parser = new();

    private static string Xml(string formData) =>
        "<?xml version=\"1.0\"?>"
        + "<edgarSubmission xmlns=\"http://www.sec.gov/edgar/thirteenffiler\">"
        + "  <headerData><cik>1649339</cik></headerData>"
        + "  <coverPage>"
        + "    <reportCalendarOrQuarter>09-30-2025</reportCalendarOrQuarter>"
        + "    <isAmendment>false</isAmendment>"
        + "    <filingManager><name>Scion Asset Management, LLC</name></filingManager>"
        + "  </coverPage>"
        + formData
        + "</edgarSubmission>";

    [Fact]
    public void ParseCoverPage_SummaryPagePresent_CarriesTheFilersOwnTotals()
    {
        // Scion's Q3 2025 cover page declares 8 positions totalling $1,381,198,076 — but only 7
        // of them are securities this platform tracks (the 8th is a Bruker preferred). The
        // declared totals are the only authoritative statement of what the WHOLE filing holds,
        // and this is the sole place the realtime lane can pick them up: skip them here and every
        // surface is back to presenting a tracked subset as the filing.
        var filing = Parser.ParseCoverPage(
            Xml(
                "  <formData><summaryPage>"
                    + "    <otherIncludedManagersCount>0</otherIncludedManagersCount>"
                    + "    <tableEntryTotal>8</tableEntryTotal>"
                    + "    <tableValueTotal>1381198076</tableValueTotal>"
                    + "  </summaryPage></formData>"
            ),
            "0001649339-25-000007",
            "1649339",
            new DateOnly(2025, 11, 3)
        );

        filing.TableEntryTotal.Should().Be(8);
        filing.TableValueTotal.Should().Be(1_381_198_076L);
    }

    [Fact]
    public void ParseCoverPage_NoSummaryPage_LeavesTotalsNull()
    {
        // A 13F-NT has no summary page at all. The totals must stay null — "the filing declares
        // nothing" — rather than zero, which would read as a filer declaring an empty book.
        var filing = Parser.ParseCoverPage(
            Xml(string.Empty),
            "0001649339-25-000008",
            "1649339",
            new DateOnly(2025, 11, 3)
        );

        filing.TableEntryTotal.Should().BeNull();
        filing.TableValueTotal.Should().BeNull();
    }

    [Fact]
    public void ParseCoverPage_MalformedTotals_AreDroppedNotZeroed()
    {
        // Filer-controlled text: a blank or non-numeric cell must fall back to null, never to a
        // parsed 0 that downstream would treat as a real declaration.
        var filing = Parser.ParseCoverPage(
            Xml(
                "  <formData><summaryPage>"
                    + "    <tableEntryTotal></tableEntryTotal>"
                    + "    <tableValueTotal>n/a</tableValueTotal>"
                    + "  </summaryPage></formData>"
            ),
            "0001649339-25-000009",
            "1649339",
            new DateOnly(2025, 11, 3)
        );

        filing.TableEntryTotal.Should().BeNull();
        filing.TableValueTotal.Should().BeNull();
    }
}
