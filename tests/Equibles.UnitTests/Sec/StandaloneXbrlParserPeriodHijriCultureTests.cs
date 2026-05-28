using System.Globalization;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class StandaloneXbrlParserPeriodHijriCultureTests
{
    private const string Namespaces =
        "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\"";

    // XBRL xbrli:startDate / endDate are xs:date (ISO yyyy-MM-dd) per the XBRL
    // 2.1 spec, so a duration context must parse to the same DateOnly on any
    // host. TryParsePeriod parses both via bare DateOnly.TryParse with no
    // InvariantCulture, so under ar-SA (Umm al-Qura) the ISO dates fail to
    // parse, TryParsePeriod returns false, BuildContextMap drops the context,
    // and every fact referencing it is silently discarded — a Hijri-locale
    // host would ingest zero financial facts from otherwise-valid filings.
    // Pin: a valid duration fact survives parsing and keeps its ISO period
    // regardless of thread culture.
    [Fact(
        Skip = "GH-2652 — TryParsePeriod omits InvariantCulture; ISO xs:date periods drop facts under ar-SA"
    )]
    public void Parse_DurationContextUnderHijriCulture_EmitsFactWithIsoPeriod()
    {
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"http://www.sec.gov/CIK\">0000320193</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:startDate>2020-01-01</xbrli:startDate><xbrli:endDate>2020-12-31</xbrli:endDate></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "<us-gaap:Revenues contextRef=\"C1\" unitRef=\"u\">1000</us-gaap:Revenues>"
            + "</xbrli:xbrl>";

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var facts = new StandaloneXbrlParser().Parse(xml);

            facts.Should().ContainSingle();
            facts[0].PeriodStart.Should().Be(new DateOnly(2020, 1, 1));
            facts[0].PeriodEnd.Should().Be(new DateOnly(2020, 12, 31));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
