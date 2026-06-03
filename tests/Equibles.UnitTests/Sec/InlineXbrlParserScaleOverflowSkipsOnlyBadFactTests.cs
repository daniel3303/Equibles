using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlParserScaleOverflowSkipsOnlyBadFactTests
{
    private const string DocOpen =
        "<html "
        + "xmlns=\"http://www.w3.org/1999/xhtml\" "
        + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
        + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\""
        + "><body><div style=\"display:none\"><ix:header><ix:resources>";

    private const string ResourcesEnd = "</ix:resources></ix:header></div>";
    private const string DocClose = "</body></html>";

    [Fact]
    public void Parse_OverflowScaleFactAlongsideGoodFact_DropsOnlyTheBadFact()
    {
        // Contract: a scale that drives the multiplier past decimal.MaxValue is malformed —
        // the parser must SKIP that fact AND CONTINUE. The sibling test only asserts no-throw;
        // it can't tell "bad fact dropped, good fact kept" from "whole parse aborted" or "bad
        // fact silently included". Pin both: the good fact survives, the overflow one is gone.
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:Revenues\" contextRef=\"C1\" unitRef=\"u\">5</ix:nonFraction>"
            + "<ix:nonFraction name=\"us-gaap:Assets\" contextRef=\"C1\" unitRef=\"u\" scale=\"29\">1</ix:nonFraction>"
            + DocClose;

        var facts = new InlineXbrlParser().Parse(html);

        facts.Should().ContainSingle();
        facts[0].Tag.Should().Be("Revenues");
        facts[0].Value.Should().Be(5m);
    }
}
