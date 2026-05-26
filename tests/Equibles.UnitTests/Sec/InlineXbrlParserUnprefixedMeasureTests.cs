using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlParserUnprefixedMeasureTests
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

    // Sibling to InlineXbrlParserEmptyMeasureLocalNameTests (prefix-with-empty-local
    // → drop) and InlineXbrlParserDivideUnitEmptyDenominatorTests. StripPrefix has a
    // third branch — input with NO colon — that returns the qname verbatim. The
    // existing inline-XBRL pins always feed a prefixed measure ("iso4217:USD"),
    // so the unprefixed arm is unhit even though SEC-extension instances
    // occasionally emit `<xbrli:measure>USD</xbrli:measure>` without a taxonomy
    // prefix. The parser must preserve the measure as the unit verbatim; a
    // refactor that required a colon would drop every fact referencing such a
    // unit. (Standalone-XBRL parser has the same contract; this is the inline
    // sibling, separate code path.)
    [Fact]
    public void Parse_MeasureIsUnprefixedQName_PreservesMeasureAsUnit()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2024-06-30</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<p><ix:nonFraction name=\"us-gaap:Assets\" contextRef=\"C1\" unitRef=\"u\" decimals=\"-6\">100000</ix:nonFraction></p>"
            + DocClose;

        var facts = new InlineXbrlParser().Parse(html);

        facts.Should().ContainSingle();
        facts[0].Unit.Should().Be("USD");
    }
}
