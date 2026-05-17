using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Filing13FXmlParserParseCoverPageTests
{
    [Fact]
    public void ParseCoverPage_NamespacedAmendmentWithDecoyOtherManagerCik_ParsesFilerNotDecoy()
    {
        // Real SEC primary_doc.xml is namespaced (edgar/thirteenffiler + a com:
        // address namespace) and an otherManager block carries its OWN <cik>.
        // The reconciliation contract requires: PeriodOfReport parsed from the
        // MM-DD-YYYY cover-page date exactly (it is part of the upsert key), the
        // filer CIK taken from headerData — never the decoy otherManager CIK —
        // and isAmendment recognised. A namespace- or scope-regression here
        // silently mis-keys or misattributes every real-time holding.
        const string xml = """
            <edgarSubmission xmlns="http://www.sec.gov/edgar/thirteenffiler" xmlns:com="http://www.sec.gov/edgar/common">
              <headerData>
                <submissionType>13F-HR/A</submissionType>
                <filerInfo>
                  <filer><credentials><cik>0001067983</cik><ccc>XXXXXXXX</ccc></credentials></filer>
                </filerInfo>
              </headerData>
              <formData>
                <coverPage>
                  <reportCalendarOrQuarter>09-30-2025</reportCalendarOrQuarter>
                  <isAmendment>true</isAmendment>
                  <filingManager>
                    <name>BERKSHIRE HATHAWAY INC</name>
                    <address>
                      <com:city>OMAHA</com:city>
                      <com:stateOrCountry>NE</com:stateOrCountry>
                    </address>
                  </filingManager>
                  <form13FFileNumber>028-12345</form13FFileNumber>
                  <otherManagers2Info>
                    <otherManager2>
                      <sequenceNumber>1</sequenceNumber>
                      <otherManager><cik>0009999999</cik><name>DECOY ADVISORS LLC</name></otherManager>
                    </otherManager2>
                  </otherManagers2Info>
                </coverPage>
              </formData>
            </edgarSubmission>
            """;

        var parser = new Filing13FXmlParser();

        var filing = parser.ParseCoverPage(
            xml,
            accessionNumber: "0001067983-25-000200",
            cik: "0001067983",
            filingDate: new DateOnly(2025, 11, 14)
        );

        filing.PeriodOfReport.Should().Be(new DateOnly(2025, 9, 30));
        filing.IsAmendment.Should().BeTrue();
        filing.Cik.Should().Be("1067983");
        filing.FilingManagerName.Should().Be("BERKSHIRE HATHAWAY INC");
        filing.City.Should().Be("OMAHA");
        filing.OtherManagers.Should().ContainKey(1).WhoseValue.Should().Be("DECOY ADVISORS LLC");
    }
}
