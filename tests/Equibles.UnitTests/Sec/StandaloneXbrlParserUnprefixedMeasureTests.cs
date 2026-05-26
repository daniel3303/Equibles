using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Sibling to the PrefixOnlyMeasure / DivideUnitMissingMeasure / EmptyMeasureValue
/// pins. ParsedXbrlFact.Unit's contract: "single measures collapse to their local
/// name (iso4217:USD → USD)". An unprefixed measure like &lt;xbrli:measure&gt;USD
/// &lt;/xbrli:measure&gt; has no colon at all — the whole value IS the local name.
/// The parser must preserve it verbatim. A refactor that "required" a colon and
/// returned null for unprefixed measures (e.g. switching IndexOf to LastIndexOf-with-
/// guard) would drop every fact referencing such a unit. Unprefixed measures are
/// rare in compliant filings but appear in some filer-extension instances.
/// </summary>
public class StandaloneXbrlParserUnprefixedMeasureTests
{
    [Fact]
    public void Parse_MeasureIsUnprefixedQName_PreservesMeasureAsUnit()
    {
        var xml =
            "<xbrli:xbrl "
            + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
            + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\">"
            + "<xbrli:context id=\"C1\">"
            + "<xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "<xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>USD</xbrli:measure></xbrli:unit>"
            + "<us-gaap:Revenues contextRef=\"C1\" unitRef=\"u\">100</us-gaap:Revenues>"
            + "</xbrli:xbrl>";

        var facts = new StandaloneXbrlParser().Parse(xml);

        facts.Should().ContainSingle();
        facts[0].Unit.Should().Be("USD");
    }
}
