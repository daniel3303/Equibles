using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// NormaliseDigits is documented to strip not just '$' but the other currency
/// glyphs (€, £, ¥) and the Unicode space separators filers use as thousands
/// groupers — NBSP (U+00A0), narrow NBSP (U+202F) and thin space (U+2009) —
/// before parsing. CurrencyGlyphTests only exercises the '$' + regular-space
/// path; drop any one of these other cases and decimal.TryParse fails on the
/// residual glyph, so the fact is silently dropped instead of decoded. Pins
/// the strip-then-parse path for a non-'$' currency and all three Unicode
/// spaces at once: '£1·234·567·890.12' must decode to 1234567890.12.
/// </summary>
public class InlineXbrlParserUnicodeSpaceGlyphTests
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
    public void Parse_ValueWithPoundGlyphAndUnicodeSpaceSeparators_StripsAllAndDecodes()
    {
        // £ (U+00A3) + NBSP (U+00A0) + narrow NBSP (U+202F) + thin space (U+2009)
        // grouping a 10-digit value with a '.' decimal: contract says all glyphs
        // are stripped, leaving "1234567890.12" for InvariantCulture parsing.
        var value = "£1 234 567 890.12";
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-06-30</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:GBP</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:Revenues\" contextRef=\"C1\" unitRef=\"u\" format=\"ixt:numdotdecimal\">"
            + value
            + "</ix:nonFraction>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Value.Should().Be(1234567890.12m);
        fact.Unit.Should().Be("GBP");
    }
}
