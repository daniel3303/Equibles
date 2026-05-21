using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Filing13FXmlParserOtherManagersNoCoverPageFallbackTests
{
    // ParseCoverPage scopes otherManager2 lookups via `coverPage ?? root` — the
    // null-coalescing fallback (line 58) is uncovered. A malformed primary_doc
    // that omits the <coverPage> wrapper but still ships <otherManagers2Info>
    // (e.g., a partial filing or future schema variant) must still pick up
    // the manager seq → name pairs by widening the scope to the root.
    // A regression that hard-coded `Descendants(coverPage, "otherManager2")`
    // would compile, pass every cover-page test, and silently lose every
    // co-manager for filings missing the coverPage wrapper.
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
