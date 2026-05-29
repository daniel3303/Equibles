using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlParserScaleWithSignNegativeTests
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
    public void Parse_ScaleAndSignNegativeTogether_ScalesMagnitudeThenNegatesOnce()
    {
        // scale and sign="-" compose: scale multiplies the magnitude, sign makes
        // it negative — they must NOT cancel. "5" with scale="6" sign="-" is
        // -5,000,000. Existing pins cover scale alone and sign alone; this pins
        // their combination (contrast GH-2799 where parens + sign wrongly cancel).
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:NetIncomeLoss\" contextRef=\"C1\" unitRef=\"u\" scale=\"6\" sign=\"-\">5</ix:nonFraction>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Value.Should().Be(-5_000_000m);
    }
}
