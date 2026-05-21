using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class StandaloneXbrlParserNonNumericValueTests
{
    private const string Namespaces =
        "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\"";

    // TryParseFact wraps the value extraction in
    // `decimal.TryParse(element.Value, NumberStyles.Float, InvariantCulture, …)`.
    // Existing tests cover well-formed numeric values and the
    // missing-contextRef / missing-unitRef / xsi:nil guards; the
    // non-numeric-text guard (line 251) is uncovered. A fact element whose
    // body fails to parse as a decimal must be dropped silently — never
    // throw, never produce a fact with `Value = 0`. A regression that
    // loosened the guard to `NumberStyles.Any` (accepting "$1,234") or that
    // skipped the TryParse check would corrupt the financial-facts pipeline
    // with garbage values.
    [Fact]
    public void Parse_FactValueIsNotANumber_FactIsDropped()
    {
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"http://www.sec.gov/CIK\">0000320193</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2018-09-29</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"usd\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "<us-gaap:Revenues contextRef=\"C1\" unitRef=\"usd\">n/a</us-gaap:Revenues>"
            + "</xbrli:xbrl>";

        new StandaloneXbrlParser().Parse(xml).Should().BeEmpty();
    }
}
