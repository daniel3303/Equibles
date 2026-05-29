using System.Globalization;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class StandaloneXbrlParserPeriodCultureInvarianceTests
{
    private const string Namespaces =
        "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\"";

    // XBRL xbrli:instant dates are Gregorian ISO 8601 (xs:date) per spec, so
    // "2024-01-31" must always resolve to 2024-01-31 regardless of host culture.
    // TryParsePeriod calls DateOnly.TryParse(instant.Value, ...) with no
    // InvariantCulture; under th-TH (ThaiBuddhist calendar, +543-year era) the
    // ambient parser reads "2024" as a Buddhist-era year (-> 1481 CE), feeding
    // a corrupt period into the financial-facts pipeline on a non-US host.
    [Fact]
    public void Parse_InstantDateUnderThaiBuddhistCulture_ResolvesGregorianIsoDate()
    {
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"http://www.sec.gov/CIK\">0000320193</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2024-01-31</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "<us-gaap:Revenues contextRef=\"C1\" unitRef=\"u\">1000</us-gaap:Revenues>"
            + "</xbrli:xbrl>";

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("th-TH");
        try
        {
            var fact = new StandaloneXbrlParser().Parse(xml).Should().ContainSingle().Subject;

            fact.IsInstant.Should().BeTrue();
            fact.PeriodEnd.Should().Be(new DateOnly(2024, 1, 31));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
