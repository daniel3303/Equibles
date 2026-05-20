using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlParserNilHandlingTests
{
    private const string DocOpen =
        "<html "
        + "xmlns=\"http://www.w3.org/1999/xhtml\" "
        + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
        + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:xbrldi=\"http://xbrl.org/2006/xbrldi\" "
        + "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\""
        + "><body><div style=\"display:none\"><ix:header><ix:resources>";

    private const string ResourcesEnd = "</ix:resources></ix:header></div>";
    private const string DocClose = "</body></html>";

    [Fact]
    public void Parse_NonFractionWithXsiNilTrue_SkipsFact()
    {
        // iXBRL contract: xsi:nil="true" marks a fact as having no value
        // (a placeholder for "this concept was filed without a value").
        // Extracting it would silently misreport the filing — e.g. surfacing
        // "0" where the issuer explicitly declined to report a number.
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:Revenues\" contextRef=\"C1\" unitRef=\"u\" xsi:nil=\"true\">0</ix:nonFraction>"
            + DocClose;

        new InlineXbrlParser().Parse(html).Should().BeEmpty();
    }
}
