using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Filing13FXmlParserParseInformationTableTests
{
    [Fact]
    public void ParseInformationTable_TwoNamespacedRows_ExtractsEachRowWithoutCrossContamination()
    {
        // Contract: every <infoTable> row is parsed independently and
        // namespace-agnostically. A field (votingAuthority, shares, putCall)
        // must come from its OWN row — recursive descent that leaked a
        // sibling's value would silently misattribute holdings on the
        // reconciliation-critical path. Also: comma-formatted sshPrnamt parses.
        const string xml = """
            <informationTable xmlns="http://www.sec.gov/edgar/document/thirteenf/informationtable">
              <infoTable>
                <nameOfIssuer>APPLE INC</nameOfIssuer>
                <titleOfClass>COM</titleOfClass>
                <cusip>037833100</cusip>
                <value>1000</value>
                <shrsOrPrnAmt><sshPrnamt>1,000</sshPrnamt><sshPrnamtType>SH</sshPrnamtType></shrsOrPrnAmt>
                <putCall>Put</putCall>
                <investmentDiscretion>SOLE</investmentDiscretion>
                <otherManager>4</otherManager>
                <votingAuthority><Sole>100</Sole><Shared>5</Shared><None>0</None></votingAuthority>
              </infoTable>
              <infoTable>
                <nameOfIssuer>MICROSOFT CORP</nameOfIssuer>
                <titleOfClass>COM</titleOfClass>
                <cusip>594918104</cusip>
                <value>2000</value>
                <shrsOrPrnAmt><sshPrnamt>2000</sshPrnamt><sshPrnamtType>SH</sshPrnamtType></shrsOrPrnAmt>
                <investmentDiscretion>DFND</investmentDiscretion>
                <votingAuthority><Sole>200</Sole><Shared>0</Shared><None>0</None></votingAuthority>
              </infoTable>
            </informationTable>
            """;

        var holdings = new Filing13FXmlParser().ParseInformationTable(xml);

        holdings.Should().HaveCount(2);

        var apple = holdings[0];
        apple.Cusip.Should().Be("037833100");
        apple.Shares.Should().Be(1000);
        apple.ShareType.Should().Be("SH");
        apple.PutCall.Should().Be("Put");
        apple.OtherManagerNumber.Should().Be(4);
        apple.VotingAuthSole.Should().Be(100);
        apple.VotingAuthShared.Should().Be(5);

        var microsoft = holdings[1];
        microsoft.Cusip.Should().Be("594918104");
        microsoft.VotingAuthSole.Should().Be(200);
        microsoft.PutCall.Should().BeNullOrEmpty();
        microsoft.OtherManagerNumber.Should().BeNull();
    }
}
