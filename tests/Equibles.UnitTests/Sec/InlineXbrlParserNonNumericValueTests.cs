using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlParserNonNumericValueTests
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

    // Contract: TryDecodeValue is a Try* decoder — non-numeric content that is
    // NOT a zero-format placeholder must fail (return false), so Parse drops the
    // fact rather than fabricating one. "N/A" survives glyph normalisation but is
    // not a parseable decimal; a decoder that swallowed the parse failure and
    // emitted 0 would inject a spurious $0 revenue fact into every filing that
    // uses a textual placeholder. The element is otherwise fully valid (context,
    // unit, name) so the ONLY reason it can be dropped is the failed decode.
    [Fact]
    public void Parse_NonFractionWithNonNumericContent_ProducesNoFact()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:Revenues\" contextRef=\"C1\" unitRef=\"u\">N/A</ix:nonFraction>"
            + DocClose;

        var facts = new InlineXbrlParser().Parse(html);

        facts.Should().BeEmpty();
    }
}
