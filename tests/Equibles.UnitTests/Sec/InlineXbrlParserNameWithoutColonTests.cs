using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlParserNameWithoutColonTests
{
    private const string DocOpen =
        "<html "
        + "xmlns=\"http://www.w3.org/1999/xhtml\" "
        + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
        + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\" "
        + "><body><div style=\"display:none\"><ix:header><ix:resources>";

    private const string ResourcesEnd = "</ix:resources></ix:header></div>";

    private const string DocClose = "</body></html>";

    // iXBRL fact elements carry name="prefix:localName" (e.g., "us-gaap:Assets").
    // A name without a colon cannot be split into taxonomy + tag and must be
    // silently dropped — not recorded with an empty taxonomy or the full name
    // as tag.
    [Fact]
    public void Parse_FactNameWithoutColonSeparator_DropsFactSilently()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2024-06-30</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<p><ix:nonFraction name=\"Assets\" contextRef=\"C1\" unitRef=\"u\" decimals=\"-6\">500000</ix:nonFraction></p>"
            + DocClose;

        var facts = new InlineXbrlParser().Parse(html);

        facts.Should().BeEmpty();
    }
}
