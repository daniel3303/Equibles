using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Filing13FXmlParserCikFallbackTests
{
    private readonly Filing13FXmlParser _sut = new();

    // Contract (doc-comment): accessionNumber and cik come from the daily index and are used as
    // fallbacks when the XML omits them. When headerData carries no <cik>, the zero-padded index
    // cik must be adopted with its leading zeros trimmed to the canonical form. Both existing
    // cover-page tests embed an XML <cik>, so only the XML-cik path is pinned; this pins fallback.
    [Fact]
    public void ParseCoverPage_XmlOmitsCik_FallsBackToIndexCikWithLeadingZerosTrimmed()
    {
        var xml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<edgarSubmission xmlns=\"http://www.sec.gov/edgar/thirteenffiler\">"
            + "  <headerData></headerData>"
            + "  <coverPage>"
            + "    <reportCalendarOrQuarter>12-31-2024</reportCalendarOrQuarter>"
            + "    <filingManager><name>Test Manager LLC</name></filingManager>"
            + "  </coverPage>"
            + "</edgarSubmission>";

        var filing = _sut.ParseCoverPage(
            xml,
            accessionNumber: "0001234567-24-000001",
            cik: "0001234567",
            filingDate: new DateOnly(2025, 2, 14)
        );

        filing.Cik.Should().Be("1234567");
    }
}
