using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// numcommadecimal (European decimal: '.' groups thousands, ',' is the decimal
/// point) and the iXBRL scale multiplier are independent steps in
/// TryDecodeValue, each pinned alone and in other pairs (parens+commaDecimal,
/// parens+scale, scale+sign) — but the European-decimal-with-scale pair is not,
/// despite being routine for IFRS filers reporting "in thousands/millions".
/// The contract: decode the European value, THEN apply scale. A refactor that
/// applied scale to the pre-normalised digits, or that swapped the decimal
/// separator after scaling, would corrupt the value only when both fire.
/// </summary>
public class InlineXbrlParserCommaDecimalWithScaleTests
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
        + "xmlns:dei=\"http://xbrl.sec.gov/dei/2018-01-31\" "
        + "xmlns:srt=\"http://fasb.org/srt/2018-01-31\" "
        + "xmlns:ifrs-full=\"http://xbrl.ifrs.org/taxonomy/2021-03-24\""
        + "><body><div style=\"display:none\"><ix:header><ix:resources>";

    private const string ResourcesEnd = "</ix:resources></ix:header></div>";

    private const string DocClose = "</body></html>";

    [Fact]
    public void Parse_CommaDecimalValueWithScale_NormalisesEuropeanDecimalThenScales()
    {
        // "2.500,75" is European 2500.75 (dot groups thousands, comma is the
        // decimal point); scale="3" then multiplies by 10^3 → 2,500,750.
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-06-30</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:EUR</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"ifrs-full:Revenue\" contextRef=\"C1\" unitRef=\"u\" format=\"ixt:numcommadecimal\" scale=\"3\">2.500,75</ix:nonFraction>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Value.Should().Be(2_500_750m);
        fact.Unit.Should().Be("EUR");
    }
}
