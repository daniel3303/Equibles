using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlParserDivideUnitEmptyDenominatorTests
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

    // ResolveUnit handles XBRL divide units (e.g. USD-per-share). The contract
    // is documented in the body and the StripPrefix doc-comment: when either
    // half of the ratio is empty or unresolvable, the unit string is null and
    // the fact must be silently dropped — emitting "USD/" or "/" would corrupt
    // the FinancialFact stream because downstream consumers split on '/' to
    // recognize ratio units. A refactor that removed the IsNullOrEmpty guard
    // on denominatorMeasure (only kept the numerator check, or used && instead
    // of ||) would compile and silently feed malformed ratio units through.
    [Fact]
    public void Parse_DivideUnitWithEmptyDenominatorMeasure_DropsFactSilently()
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
            + "    <xbrli:unitDenominator><xbrli:measure></xbrli:measure></xbrli:unitDenominator>"
            + "  </xbrli:divide>"
            + "</xbrli:unit>"
            + ResourcesEnd
            + "<p><ix:nonFraction name=\"us-gaap:EarningsPerShare\" contextRef=\"C1\" unitRef=\"perShare\" decimals=\"2\">1.25</ix:nonFraction></p>"
            + DocClose;

        var facts = new InlineXbrlParser().Parse(html);

        facts.Should().BeEmpty();
    }
}
