using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Parenthesised accounting-negatives and the scale attribute are independent
/// code paths in TryDecodeValue: parentheses flip the sign during decode,
/// scale multiplies by 10^scale afterwards in TryApplyScaleAndSign. Each is
/// pinned alone (scale on a positive value; parentheses with no scale), but a
/// negative parenthesised value carrying a scale — a routine "(2,500)" in
/// thousands — exercises both in one fact. Contract: the scale must apply to
/// the already-negated value, yielding -2,500 × 10^3 = -2,500,000. A refactor
/// that applied scale before the parenthesis sign flip, or dropped one step,
/// would break silently otherwise.
/// </summary>
public class InlineXbrlParserParenthesisedWithScaleTests
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
    public void Parse_ParenthesisedValueWithScale_AppliesScaleToNegatedValue()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-06-30</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:NetIncomeLoss\" contextRef=\"C1\" unitRef=\"u\" format=\"ixt:numdotdecimal\" scale=\"3\">(2,500)</ix:nonFraction>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Value.Should().Be(-2_500_000m);
    }
}
