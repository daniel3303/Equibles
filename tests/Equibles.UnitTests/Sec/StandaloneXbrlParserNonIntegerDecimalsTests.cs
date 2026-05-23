using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class StandaloneXbrlParserNonIntegerDecimalsTests
{
    private const string Namespaces =
        "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\"";

    // Contract: ParseDecimals returns null for unparseable non-"INF" values.
    // A malformed decimals attribute must not prevent fact extraction — the
    // value and period are still valid; only precision metadata is lost.
    [Fact]
    public void Parse_FactWithNonIntegerDecimalsAttribute_ExtractsFactWithNullDecimals()
    {
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2024-06-30</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "<us-gaap:Assets contextRef=\"C1\" unitRef=\"u\" decimals=\"foo\">500000</us-gaap:Assets>"
            + "</xbrli:xbrl>";

        var fact = new StandaloneXbrlParser().Parse(xml).Should().ContainSingle().Subject;

        fact.Value.Should().Be(500_000m);
        fact.Decimals.Should().BeNull();
    }
}
