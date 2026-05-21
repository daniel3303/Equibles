using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Filing13FXmlParserIsAmendmentYesTests
{
    [Fact]
    public void ParseCoverPage_IsAmendmentThreeLetterYes_RecognisedAsAmendment()
    {
        // IsAmendmentValue accepts four truthy aliases — true, y, yes, 1. Sibling
        // pins cover the "true", "y", and "1" branches; the bare "yes" branch is
        // the only one of the four left unpinned. A "simplify to ToLower() ==
        // \"true\"" or "collapse to StartsWith(\"y\") && Length == 1" refactor
        // would compile, pass every other IsAmendment pin, and silently
        // re-classify every legacy filing that uses the three-letter flag as an
        // original — breaking amendment de-duplication on exactly those
        // historical filings.
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
                  <isAmendment>yes</isAmendment>
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
