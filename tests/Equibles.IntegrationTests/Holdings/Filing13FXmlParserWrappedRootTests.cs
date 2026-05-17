using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Filing13FXmlParserWrappedRootTests
{
    [Fact]
    public void ParseInformationTable_InfoTableRowsNestedUnderWrapper_StillExtractedViaRecursiveFallback()
    {
        // Contract (method doc + inline comment): infoTable rows are normally
        // flat children, but the parser must "fall back to a recursive scan
        // only if the root is wrapped". Some filing agents wrap the rows in an
        // extra element; without the fallback an entire original filing would
        // silently yield zero holdings and be dropped.
        const string xml = """
            <informationTable xmlns="http://www.sec.gov/edgar/document/thirteenf/informationtable">
              <ns:document xmlns:ns="urn:agent:wrapper">
                <infoTable>
                  <nameOfIssuer>APPLE INC</nameOfIssuer>
                  <titleOfClass>COM</titleOfClass>
                  <cusip>037833100</cusip>
                  <value>1</value>
                  <shrsOrPrnAmt>
                    <sshPrnamt>12,345</sshPrnamt>
                    <sshPrnamtType>SH</sshPrnamtType>
                  </shrsOrPrnAmt>
                  <investmentDiscretion>SOLE</investmentDiscretion>
                  <votingAuthority>
                    <Sole>12345</Sole>
                    <Shared>0</Shared>
                    <None>0</None>
                  </votingAuthority>
                </infoTable>
              </ns:document>
            </informationTable>
            """;

        var holdings = new Filing13FXmlParser().ParseInformationTable(xml);

        holdings.Should().ContainSingle("a wrapped root must not lose the only holding row");
        holdings[0].Cusip.Should().Be("037833100");
        holdings[0].Shares.Should().Be(12345);
        holdings[0].VotingAuthSole.Should().Be(12345);
    }
}
