using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Filing13FXmlParserOtherManagersNoCoverPageFallbackTests
{
    // ParseCoverPage scans otherManager2 from the document root, because real
    // filings carry them under formData/summaryPage while a malformed or
    // future-variant primary_doc may omit wrappers entirely. A regression that
    // re-scopes the scan to coverPage would compile and pass any test that
    // co-locates the managers with the cover page, yet silently lose every
    // co-manager on real filings — this pin (managers with no coverPage at
    // all) fails for any scope narrower than the root.
    [Fact]
    public void ParseCoverPage_OtherManagers2InfoSiblingOfCoverPageAbsent_FallsBackToRootScope()
    {
        const string xml = """
            <edgarSubmission xmlns="http://www.sec.gov/edgar/thirteenffiler">
              <headerData>
                <submissionType>13F-HR</submissionType>
                <filerInfo>
                  <filer><credentials><cik>0001067983</cik></credentials></filer>
                </filerInfo>
              </headerData>
              <otherManagers2Info>
                <otherManager2>
                  <sequenceNumber>1</sequenceNumber>
                  <otherManager><cik>0009999999</cik><name>SOLE CO-MANAGER</name></otherManager>
                </otherManager2>
              </otherManagers2Info>
            </edgarSubmission>
            """;

        var filing = new Filing13FXmlParser().ParseCoverPage(
            xml,
            accessionNumber: "0001067983-25-000400",
            cik: "0001067983",
            filingDate: new DateOnly(2025, 11, 14)
        );

        filing.OtherManagers.Should().ContainKey(1).WhoseValue.Should().Be("SOLE CO-MANAGER");
    }
}
