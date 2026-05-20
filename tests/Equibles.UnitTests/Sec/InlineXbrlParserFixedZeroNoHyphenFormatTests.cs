using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlParserFixedZeroNoHyphenFormatTests
{
    private const string DocOpen =
        "<html "
        + "xmlns=\"http://www.w3.org/1999/xhtml\" "
        + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
        + "xmlns:ixt=\"http://www.xbrl.org/inlineXBRL/transformation/2015-02-26\" "
        + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\""
        + "><body><div style=\"display:none\"><ix:header><ix:resources>";

    private const string ResourcesEnd = "</ix:resources></ix:header></div>";

    private const string DocClose = "</body></html>";

    // Third sibling to Parse_NumDashFormat_ReturnsZero and
    // Parse_FixedZeroFormat_ReturnsZero. The WHY-comment on the zero-format
    // branch enumerates THREE Inline XBRL Transformations format hints that
    // mean "hard-coded zero" — `numdash`, `fixed-zero`, `fixedzero`. The first
    // two arms are pinned; the third (`fixedzero` without the hyphen — older
    // transformation-registry spelling found in pre-2017 filings) is the
    // tail of the `||` chain and would be silently dropped by any refactor
    // that consolidated the two fixed-zero spellings into one. A regression
    // would parse the literal placeholder content of every
    // `format="ixt:fixedzero"` fact (typically an em-dash) instead of
    // emitting 0.
    [Fact]
    public void Parse_FixedZeroNoHyphenFormat_ReturnsZero()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:Revenues\" contextRef=\"C1\" unitRef=\"u\" format=\"ixt:fixedzero\">&#8212;</ix:nonFraction>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Value.Should().Be(0m);
    }
}
