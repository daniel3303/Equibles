using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlParserDivideUnitPrefixOnlyNumeratorTests
{
    private const string DocOpen =
        "<html "
        + "xmlns=\"http://www.w3.org/1999/xhtml\" "
        + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
        + "xmlns:ixt=\"http://www.xbrl.org/inlineXBRL/transformation/2015-02-26\" "
        + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:xbrldi=\"http://xbrl.org/2006/xbrldi\" "
        + "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\" "
        + "><body><div style=\"display:none\"><ix:header><ix:resources>";

    private const string ResourcesEnd = "</ix:resources></ix:header></div>";

    private const string DocClose = "</body></html>";

    // Sibling to InlineXbrlParserDivideUnitEmptyDenominatorTests (which covers the
    // IsNullOrEmpty short-circuit on the divide-unit measures) and to
    // InlineXbrlParserEmptyMeasureLocalNameTests (which covers prefix-only QName on
    // a single, non-divide unit). The divide-unit + prefix-only-QName combination
    // is its own structurally distinct guard: StripPrefix returns null for "USD:"
    // on the NUMERATOR, so the `numeratorLocal == null` arm of the second guard
    // fires. A refactor that omitted the second guard (under the intuition that
    // the IsNullOrEmpty guard was enough — it's not, because "USD:" is non-empty
    // before StripPrefix yet null after) would produce a "/<denom>" unit string
    // that downstream consumers split on '/' would parse as a malformed ratio.
    [Fact]
    public void Parse_DivideUnitWithPrefixOnlyNumeratorMeasure_DropsFactSilently()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2024-06-30</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"perShare\">"
            + "  <xbrli:divide>"
            + "    <xbrli:unitNumerator><xbrli:measure>iso4217:</xbrli:measure></xbrli:unitNumerator>"
            + "    <xbrli:unitDenominator><xbrli:measure>xbrli:shares</xbrli:measure></xbrli:unitDenominator>"
            + "  </xbrli:divide>"
            + "</xbrli:unit>"
            + ResourcesEnd
            + "<p><ix:nonFraction name=\"us-gaap:EarningsPerShare\" contextRef=\"C1\" unitRef=\"perShare\" decimals=\"2\">1.25</ix:nonFraction></p>"
            + DocClose;

        var facts = new InlineXbrlParser().Parse(html);

        facts.Should().BeEmpty();
    }
}
