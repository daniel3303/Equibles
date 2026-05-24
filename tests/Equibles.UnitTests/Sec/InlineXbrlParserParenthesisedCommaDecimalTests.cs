using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Parenthesised (accounting-negative) and numcommadecimal (European decimal)
/// are independent code paths in TryDecodeValue — each is tested alone, but
/// the combined path is not. A refactor that reorders the parens-strip and the
/// digit-normalise steps, or that applies the sign flip before the decimal
/// swap, would break silently unless both fire in the same fact.
/// </summary>
public class InlineXbrlParserParenthesisedCommaDecimalTests
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
    public void Parse_ParenthesisedCommaDecimal_NegatesEuropeanValue()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-06-30</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:EUR</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"ifrs-full:ProfitLoss\" contextRef=\"C1\" unitRef=\"u\" format=\"ixt:numcommadecimal\">(1.234,56)</ix:nonFraction>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Value.Should().Be(-1234.56m);
        fact.Unit.Should().Be("EUR");
    }
}
