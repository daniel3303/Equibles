using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlParserParenthesisedWithSignNegativeTests
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

    [Fact(Skip = "GH-2799 — parens + sign=\"-\" double-negates to positive")]
    public void Parse_ParenthesisedValueWithSignNegative_StaysNegative()
    {
        // Accounting parentheses and the sign="-" attribute are two encodings of
        // the SAME negativity: "(500)" with sign="-" is negative five hundred.
        // They must not compound into a positive — a financial consumer would
        // read +500 as a sign error. Existing pins cover each mechanism alone
        // (Parse_ParenthesisedValue_IsNegated, Parse_SignAttributeNegative_NegatesValue);
        // this pins their combination.
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:NetIncomeLoss\" contextRef=\"C1\" unitRef=\"u\" sign=\"-\">(500)</ix:nonFraction>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Value.Should().Be(-500m);
    }
}
