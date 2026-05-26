using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class StandaloneXbrlParserEmptyMeasureValueTests
{
    // Sibling to the divide-unit / prefix-only / padded-measure pins.
    // ResolveUnit's single-measure branch trims the value and returns null
    // when it collapses to empty (`<xbrli:measure></xbrli:measure>` or
    // whitespace-only). SEC standalone XBRL .xml payloads occasionally ship
    // a unit definition where the measure element exists but its text
    // content is blank — a partial truncation of the upstream file, or a
    // re-render bug that left the element open. A refactor that "tidied"
    // the trim away — `measure?.Value` without `.Trim()` — would let
    // a whitespace-only unit through as Unit = " ", break the unique-index
    // key in FinancialFacts.Data, and corrupt downstream queries that
    // GROUP BY Unit. Pin: empty-valued single measure → fact dropped.
    [Fact]
    public void Parse_UnitWithEmptySingleMeasureValue_SkipsFactsReferencingThatUnit()
    {
        var xml =
            "<xbrli:xbrl "
            + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
            + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\">"
            + "<xbrli:context id=\"C1\">"
            + "<xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "<xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"blank\"><xbrli:measure>   </xbrli:measure></xbrli:unit>"
            + "<us-gaap:Assets contextRef=\"C1\" unitRef=\"blank\">1000000</us-gaap:Assets>"
            + "</xbrli:xbrl>";

        new StandaloneXbrlParser().Parse(xml).Should().BeEmpty();
    }
}
