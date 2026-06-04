using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlParserTrailingColonNameTests
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

    // iXBRL fact names are "prefix:localName". A name ending in the colon
    // (e.g. "us-gaap:") has an EMPTY local tag and cannot form a valid fact —
    // it must be dropped silently, never recorded with Tag="". NameWithoutColon
    // pins the no-colon case; this pins the trailing-colon boundary of the same
    // guard (an off-by-one to colonIdx >= name.Length would emit an empty tag).
    [Fact]
    public void Parse_FactNameWithTrailingColon_DropsFactSilently()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2024-06-30</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<p><ix:nonFraction name=\"us-gaap:\" contextRef=\"C1\" unitRef=\"u\" decimals=\"-6\">500000</ix:nonFraction></p>"
            + DocClose;

        var facts = new InlineXbrlParser().Parse(html);

        facts.Should().BeEmpty();
    }
}
