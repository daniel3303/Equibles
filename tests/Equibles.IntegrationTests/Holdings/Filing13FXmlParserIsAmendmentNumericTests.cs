using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Filing13FXmlParserIsAmendmentNumericTests
{
    [Fact]
    public void ParseCoverPage_IsAmendmentNumericOne_RecognisedAsAmendment()
    {
        // IsAmendmentValue accepts four truthy aliases — true, y, yes, 1 — to
        // tolerate variations across SEC's historical encodings. Existing tests
        // pin only the "true" branch; the "1" branch is the lone non-Equals
        // comparison in the chain, easy to drop in a "simplify to true/false"
        // refactor. Losing it silently re-classifies every amendment that uses
        // EDGAR's numeric flag as an original filing — breaking the amendment
        // delete-by-period path the importer relies on.
        const string xml = """
            <edgarSubmission xmlns="http://www.sec.gov/edgar/thirteenffiler">
              <headerData>
                <filerInfo>
                  <filer><credentials><cik>0001067983</cik></credentials></filer>
                </filerInfo>
              </headerData>
              <formData>
                <coverPage>
                  <reportCalendarOrQuarter>09-30-2025</reportCalendarOrQuarter>
                  <isAmendment>1</isAmendment>
                  <filingManager><name>BIG FUND</name></filingManager>
                  <form13FFileNumber>028-1</form13FFileNumber>
                </coverPage>
              </formData>
            </edgarSubmission>
            """;

        var parser = new Filing13FXmlParser();

        var filing = parser.ParseCoverPage(
            xml,
            accessionNumber: "0001067983-25-000900",
            cik: "0001067983",
            filingDate: new DateOnly(2025, 11, 20)
        );

        filing.IsAmendment.Should().BeTrue();
    }
}
