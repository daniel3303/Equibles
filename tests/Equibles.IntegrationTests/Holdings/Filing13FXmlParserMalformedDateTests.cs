using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Filing13FXmlParserMalformedDateTests
{
    [Fact]
    public void ParseCoverPage_UnparseableReportPeriod_ReturnsMinValueNotThrowOrBogusDate()
    {
        // Contract: an unparseable <reportCalendarOrQuarter> must yield
        // PeriodOfReport == DateOnly.MinValue — NOT an exception and NOT a
        // coerced/today date. The ingestion sweep relies on MinValue as the
        // "skip this filing" sentinel; a throw aborts the filing's catch path
        // and a bogus date silently mis-keys every holding in the upsert.
        const string xml = """
            <edgarSubmission xmlns="http://www.sec.gov/edgar/thirteenffiler">
              <headerData><filerInfo><filer><credentials><cik>0001067983</cik></credentials></filer></filerInfo></headerData>
              <formData><coverPage>
                <reportCalendarOrQuarter>Q3-2024</reportCalendarOrQuarter>
                <filingManager><name>BIG FUND</name></filingManager>
                <form13FFileNumber>028-1</form13FFileNumber>
              </coverPage></formData>
            </edgarSubmission>
            """;

        var parser = new Filing13FXmlParser();

        var filing = parser.ParseCoverPage(
            xml,
            accessionNumber: "0001067983-24-000900",
            cik: "0001067983",
            filingDate: new DateOnly(2024, 11, 20)
        );

        filing.PeriodOfReport.Should().Be(DateOnly.MinValue);
        // No <isAmendment> element present → must default to a plain original.
        filing.IsAmendment.Should().BeFalse();
        // Parsing still succeeds for the rest of the cover page.
        filing.FilingManagerName.Should().Be("BIG FUND");
    }
}
