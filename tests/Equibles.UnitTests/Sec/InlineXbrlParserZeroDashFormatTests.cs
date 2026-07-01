using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlParserZeroDashFormatTests
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

    // TR2/TR3 spell the dash-is-zero transform "zerodash" (TR1 says "numdash",
    // TR4+ "fixed-zero" — both already covered by sibling tests). A zerodash
    // placeholder must land as a 0-valued fact, not fail numeric parsing on
    // the em-dash and vanish: a dropped zero is indistinguishable from
    // "not reported" downstream.
    [Fact]
    public void Parse_ZeroDashFormat_YieldsZeroValuedFact()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2024-06-30</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<p><ix:nonFraction name=\"us-gaap:Goodwill\" contextRef=\"C1\" unitRef=\"u\" "
            + "format=\"ixt:zerodash\" decimals=\"-6\" scale=\"6\">—</ix:nonFraction></p>"
            + DocClose;

        var facts = new InlineXbrlParser().Parse(html);

        facts.Should().ContainSingle().Which.Value.Should().Be(0m);
    }
}
