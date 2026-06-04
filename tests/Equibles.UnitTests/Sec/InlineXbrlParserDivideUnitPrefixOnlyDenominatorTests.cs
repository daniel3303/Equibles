using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlParserDivideUnitPrefixOnlyDenominatorTests
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

    // Mirror of InlineXbrlParserDivideUnitPrefixOnlyNumeratorTests for the
    // DENOMINATOR. The divide-unit measure guard is `numeratorLocal == null ||
    // denominatorLocal == null`; the numerator sibling fires the first arm, this
    // fires the second — a prefix-only QName ("xbrli:") whose StripPrefix returns
    // null. The IsNullOrEmpty guard above does not catch it (non-empty before
    // StripPrefix, null after), so dropping the second arm would emit a "<num>/"
    // unit string that downstream '/'-splitting reads as a malformed ratio.
    [Fact]
    public void Parse_DivideUnitWithPrefixOnlyDenominatorMeasure_DropsFactSilently()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2024-06-30</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"perShare\">"
            + "  <xbrli:divide>"
            + "    <xbrli:unitNumerator><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unitNumerator>"
            + "    <xbrli:unitDenominator><xbrli:measure>xbrli:</xbrli:measure></xbrli:unitDenominator>"
            + "  </xbrli:divide>"
            + "</xbrli:unit>"
            + ResourcesEnd
            + "<p><ix:nonFraction name=\"us-gaap:EarningsPerShare\" contextRef=\"C1\" unitRef=\"perShare\" decimals=\"2\">1.25</ix:nonFraction></p>"
            + DocClose;

        var facts = new InlineXbrlParser().Parse(html);

        facts.Should().BeEmpty();
    }
}
