using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Filing13FXmlParserOtherManagersSkipTests
{
    [Fact]
    public void ParseCoverPage_OtherManager2WithNonNumericSequence_IsSkippedNotPoisoningTheMap()
    {
        // The existing cover-page pin only feeds a well-formed otherManager2.
        // ParseCoverPage keys OtherManagers by the parsed integer sequence and
        // skips entries whose sequenceNumber isn't an int. One malformed block
        // must not abort cover-page parsing (dropping the whole filing) nor
        // collide every bad entry onto key 0 — the valid sibling must survive.
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
                  <isAmendment>false</isAmendment>
                  <filingManager><name>BIG FUND</name></filingManager>
                  <form13FFileNumber>028-1</form13FFileNumber>
                  <otherManagers2Info>
                    <otherManager2>
                      <sequenceNumber>ABC</sequenceNumber>
                      <otherManager><cik>0009999999</cik><name>BAD CO</name></otherManager>
                    </otherManager2>
                    <otherManager2>
                      <sequenceNumber>3</sequenceNumber>
                      <otherManager><cik>0008888888</cik><name>GOOD ADVISORS</name></otherManager>
                    </otherManager2>
                  </otherManagers2Info>
                </coverPage>
              </formData>
            </edgarSubmission>
            """;

        var filing = new Filing13FXmlParser().ParseCoverPage(
            xml,
            accessionNumber: "0001067983-25-000300",
            cik: "0001067983",
            filingDate: new DateOnly(2025, 11, 14)
        );

        filing.OtherManagers.Should().HaveCount(1);
        filing.OtherManagers.Should().ContainKey(3).WhoseValue.Should().Be("GOOD ADVISORS");
        filing.OtherManagers.Should().NotContainKey(0);
        filing.OtherManagers.Values.Should().NotContain("BAD CO");
    }
}
