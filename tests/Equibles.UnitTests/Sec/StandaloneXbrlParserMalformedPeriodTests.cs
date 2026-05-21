using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class StandaloneXbrlParserMalformedPeriodTests
{
    private const string Namespaces =
        "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\"";

    // TryParsePeriod has three arms: instant, duration (startDate + endDate),
    // and a fall-through that returns false (lines 145-148). The first two
    // are covered by existing tests; the fall-through is uncovered. A period
    // with only <startDate> (filer error — endDate omitted) must drop the
    // context silently per `BuildContextMap`'s `if (!TryParsePeriod(...))
    // continue;` contract. A regression emitting a fact with `endDate =
    // default(DateOnly)` would silently feed corrupt period boundaries
    // into the financial-facts pipeline.
    [Fact]
    public void Parse_ContextWithStartDateButNoEndDate_FactReferencingItIsDropped()
    {
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"http://www.sec.gov/CIK\">0000320193</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:startDate>2020-01-01</xbrli:startDate></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "<us-gaap:Revenues contextRef=\"C1\" unitRef=\"u\">1000</us-gaap:Revenues>"
            + "</xbrli:xbrl>";

        new StandaloneXbrlParser().Parse(xml).Should().BeEmpty();
    }
}
