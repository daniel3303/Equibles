using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Lane B (coverage): exercises the full ParseInformationTable path — lines
// 85-120 are zero-hit today. A realistic two-row SEC 13F info-table XML
// drives through XDocument.Parse, the Children scan, the foreach loop,
// and every Child/Value/ParseLong mapping.
public class Filing13FXmlParserParseInformationTableTests
{
    private readonly Filing13FXmlParser _sut = new();

    [Fact]
    public void ParseInformationTable_TwoRows_ParsesAllFieldsCorrectly()
    {
        var xml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<informationTable xmlns=\"http://www.sec.gov/edgar/document/thirteenf/informationtable\">"
            + "  <infoTable>"
            + "    <cusip>037833100</cusip>"
            + "    <titleOfClass>COM</titleOfClass>"
            + "    <shrsOrPrnAmt>"
            + "      <sshPrnamt>915,560</sshPrnamt>"
            + "      <sshPrnamtType>SH</sshPrnamtType>"
            + "    </shrsOrPrnAmt>"
            + "    <putCall>Call</putCall>"
            + "    <investmentDiscretion>SOLE</investmentDiscretion>"
            + "    <votingAuthority>"
            + "      <Sole>800000</Sole>"
            + "      <Shared>100000</Shared>"
            + "      <None>15560</None>"
            + "    </votingAuthority>"
            + "    <otherManager>2</otherManager>"
            + "  </infoTable>"
            + "  <infoTable>"
            + "    <cusip>594918104</cusip>"
            + "    <titleOfClass>COM</titleOfClass>"
            + "    <shrsOrPrnAmt>"
            + "      <sshPrnamt>1234567</sshPrnamt>"
            + "      <sshPrnamtType>PRN</sshPrnamtType>"
            + "    </shrsOrPrnAmt>"
            + "    <investmentDiscretion>DFND</investmentDiscretion>"
            + "    <votingAuthority>"
            + "      <Sole>0</Sole>"
            + "      <Shared>0</Shared>"
            + "      <None>1234567</None>"
            + "    </votingAuthority>"
            + "  </infoTable>"
            + "</informationTable>";

        var result = _sut.ParseInformationTable(xml);

        result.Should().HaveCount(2);

        var first = result[0];
        first.Cusip.Should().Be("037833100");
        first.TitleOfClass.Should().Be("COM");
        first.Shares.Should().Be(915560);
        first.ShareType.Should().Be("SH");
        first.PutCall.Should().Be("Call");
        first.InvestmentDiscretion.Should().Be("SOLE");
        first.VotingAuthSole.Should().Be(800000);
        first.VotingAuthShared.Should().Be(100000);
        first.VotingAuthNone.Should().Be(15560);
        first.OtherManagerNumber.Should().Be(2);

        var second = result[1];
        second.Cusip.Should().Be("594918104");
        second.Shares.Should().Be(1234567);
        second.ShareType.Should().Be("PRN");
        second.PutCall.Should().BeNullOrEmpty();
        second.InvestmentDiscretion.Should().Be("DFND");
        second.VotingAuthNone.Should().Be(1234567);
        second.OtherManagerNumber.Should().BeNull();
    }
}
