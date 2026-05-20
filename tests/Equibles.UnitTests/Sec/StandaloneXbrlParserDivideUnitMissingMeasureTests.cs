using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the divide-unit missing-measure branch of StandaloneXbrlParser.Parse
/// (ResolveUnit's missing-numerator/denominator early return). A divide unit
/// without both measures cannot be resolved to a "numerator/denominator" name;
/// facts referencing it must be skipped, not emitted with Unit = null, since
/// every downstream FinancialFacts tool keys off Unit ("USD", "shares/USD")
/// to render the value column.
/// </summary>
public class StandaloneXbrlParserDivideUnitMissingMeasureTests
{
    [Fact]
    public void Parse_DivideUnitWithMissingNumeratorMeasure_SkipsFactsReferencingThatUnit()
    {
        // The unit declares <xbrli:divide> but only ships a denominator measure;
        // the numerator measure element is absent. ResolveUnit must return null
        // (unit dropped from the units map), and the fact's unitRef lookup must
        // therefore miss — yielding zero facts.
        var xml =
            "<xbrli:xbrl "
            + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
            + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\">"
            + "<xbrli:context id=\"C1\">"
            + "<xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "<xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"bad\">"
            + "<xbrli:divide>"
            + "<xbrli:unitNumerator></xbrli:unitNumerator>"
            + "<xbrli:unitDenominator><xbrli:measure>xbrli:shares</xbrli:measure></xbrli:unitDenominator>"
            + "</xbrli:divide>"
            + "</xbrli:unit>"
            + "<us-gaap:EarningsPerShareBasic contextRef=\"C1\" unitRef=\"bad\">12.01</us-gaap:EarningsPerShareBasic>"
            + "</xbrli:xbrl>";

        new StandaloneXbrlParser().Parse(xml).Should().BeEmpty();
    }
}
