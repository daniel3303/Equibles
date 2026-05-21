using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlParserMalformedPeriodTests
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

    // TryParsePeriod has three arms: instant, duration (startDate + endDate),
    // and a fall-through that returns false. The instant and duration arms are
    // covered by existing tests; the fall-through is uncovered. A period with
    // a lone <startDate> (a filer error) must drop the context silently — and
    // therefore any fact pointing at that context — rather than throw or
    // emit a half-baked fact with a default-DateOnly endDate. The
    // calling code's `if (!TryParsePeriod(...)) continue;` documents exactly
    // this graceful-skip contract.
    [Fact]
    public void Parse_ContextWithStartDateButNoEndDate_FactReferencingItIsDropped()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:startDate>2020-01-01</xbrli:startDate></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:Revenues\" contextRef=\"C1\" unitRef=\"u\">1000</ix:nonFraction>"
            + DocClose;

        new InlineXbrlParser().Parse(html).Should().BeEmpty();
    }
}
