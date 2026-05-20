using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Filing13FXmlParserIsAmendmentYTests
{
    [Fact]
    public void ParseCoverPage_IsAmendmentSingleLetterY_RecognisedAsAmendment()
    {
        // IsAmendmentValue accepts four truthy aliases — true, y, yes, 1. The
        // existing tests pin the "true" and "1" branches; the bare "y" branch
        // is the easiest to silently drop in a "simplify to ToLower() == "true""
        // refactor, which would re-classify every legacy filing that uses the
        // single-letter flag as an original and break amendment de-duplication.
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
                  <isAmendment>y</isAmendment>
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
