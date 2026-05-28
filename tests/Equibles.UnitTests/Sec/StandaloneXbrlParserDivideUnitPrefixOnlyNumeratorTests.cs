using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Adversarial Lane A. <c>ResolveUnit</c> has TWO structurally distinct
/// guards for an unresolvable divide unit:
/// <list type="number">
/// <item>numerator/denominator measure VALUE empty (line 200-201)</item>
/// <item><c>StripPrefix</c> returned null because the QName had a prefix
/// but no local part, e.g. <c>"iso4217:"</c> (line 204-205)</item>
/// </list>
/// The sibling <c>DivideUnitMissingMeasureTests</c> only pins guard (1).
/// A refactor that collapsed both into a single "numeratorLocal is null"
/// check using <c>?.</c> on a still-prefixed value would silently emit
/// <c>"/&lt;denom&gt;"</c> as the unit string — every EPS row would render
/// with a leading slash and break downstream parsers keyed on the unit.
/// </summary>
public class StandaloneXbrlParserDivideUnitPrefixOnlyNumeratorTests
{
    [Fact]
    public void Parse_DivideUnitWithPrefixOnlyNumeratorMeasure_SkipsFactsReferencingThatUnit()
    {
        // Numerator measure value is "iso4217:" — non-empty string, so the
        // line-200 IsNullOrEmpty guard does NOT fire; only the StripPrefix
        // null branch (line 204) catches it. Hits a different arm than the
        // empty-numerator-element sibling.
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
            + "<xbrli:unitNumerator><xbrli:measure>iso4217:</xbrli:measure></xbrli:unitNumerator>"
            + "<xbrli:unitDenominator><xbrli:measure>xbrli:shares</xbrli:measure></xbrli:unitDenominator>"
            + "</xbrli:divide>"
            + "</xbrli:unit>"
            + "<us-gaap:EarningsPerShareBasic contextRef=\"C1\" unitRef=\"bad\">12.01</us-gaap:EarningsPerShareBasic>"
            + "</xbrli:xbrl>";

        new StandaloneXbrlParser().Parse(xml).Should().BeEmpty();
    }
}
