using System.Globalization;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlParserPeriodCultureInvarianceTests
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

    // Sibling of GH-2670 (StandaloneXbrlParser): InlineXbrlParser.TryParseChildDate
    // resolves xbrli:instant via DateOnly.TryParse(text, ...) with no
    // InvariantCulture. XBRL dates are Gregorian ISO 8601 (xs:date), so
    // "2024-01-31" must resolve to 2024-01-31 on any host; under th-TH
    // (ThaiBuddhist, +543-year era) the ambient parser reads "2024" as a
    // Buddhist-era year (-> 1481 CE), corrupting the persisted period.
    [Fact]
    public void Parse_InstantDateUnderThaiBuddhistCulture_ResolvesGregorianIsoDate()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2024-01-31</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:Revenues\" contextRef=\"C1\" unitRef=\"u\">1000</ix:nonFraction>"
            + DocClose;

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("th-TH");
        try
        {
            var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

            fact.IsInstant.Should().BeTrue();
            fact.PeriodEnd.Should().Be(new DateOnly(2024, 1, 31));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
