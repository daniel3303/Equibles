using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// TryDecodeValue is a Try* decoder: a value that normalises to nothing must
/// yield NO fact, not a fabricated 0. NormaliseDigits strips currency/space
/// glyphs, so a value that is ONLY a currency glyph ("$") with no numdash/
/// fixed-zero format hint reduces to an empty string and cannot be decoded.
/// Existing pins feed glyph+digits ("$ 1 234" → 1234) or non-numeric text that
/// survives normalisation ("N/A" → parse-fail); none reduces to empty AFTER
/// stripping. Pin the empty-after-normalisation rejection.
/// </summary>
public class InlineXbrlParserGlyphOnlyValueTests
{
    private const string DocOpen =
        "<html "
        + "xmlns=\"http://www.w3.org/1999/xhtml\" "
        + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
        + "xmlns:ixt=\"http://www.xbrl.org/inlineXBRL/transformation/2015-02-26\" "
        + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:xbrldi=\"http://xbrl.org/2006/xbrldi\" "
        + "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\""
        + "><body><div style=\"display:none\"><ix:header><ix:resources>";

    private const string ResourcesEnd = "</ix:resources></ix:header></div>";

    private const string DocClose = "</body></html>";

    [Fact]
    public void Parse_ValueIsCurrencyGlyphOnly_SkipsFact()
    {
        // "$" with no format hint → NormaliseDigits strips it to "" → undecodable.
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-06-30</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:Revenues\" contextRef=\"C1\" unitRef=\"u\">$</ix:nonFraction>"
            + DocClose;

        new InlineXbrlParser().Parse(html).Should().BeEmpty();
    }
}
