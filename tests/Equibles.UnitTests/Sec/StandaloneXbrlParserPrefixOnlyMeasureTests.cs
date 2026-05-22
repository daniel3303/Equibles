using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Sibling to StandaloneXbrlParserDivideUnitMissingMeasureTests. The
/// contract — pinned by that test's WHY-comment — is that a fact whose
/// referenced unit cannot be resolved to a usable name must be skipped,
/// because every downstream FinancialFacts tool keys off Unit ("USD",
/// "shares/USD") to render the value column. A measure of the form
/// "iso4217:" (prefix retained, local name empty) is a malformed QName
/// with no usable measure portion, so the unit is unusable and any fact
/// referencing it must be dropped.
/// </summary>
public class StandaloneXbrlParserPrefixOnlyMeasureTests
{
    [Fact]
    public void Parse_MeasureIsPrefixOnlyQName_SkipsFactsReferencingThatUnit()
    {
        // The unit's <xbrli:measure> is "iso4217:" — well-formed XML, but
        // the QName's local name (the part after the colon) is empty. The
        // unit is therefore unresolvable to a usable measure string, and
        // the fact referencing it must be dropped — yielding zero facts,
        // matching the existing missing-measure contract.
        var xml =
            "<xbrli:xbrl "
            + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
            + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\">"
            + "<xbrli:context id=\"C1\">"
            + "<xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "<xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"bad\"><xbrli:measure>iso4217:</xbrli:measure></xbrli:unit>"
            + "<us-gaap:Revenues contextRef=\"C1\" unitRef=\"bad\">100</us-gaap:Revenues>"
            + "</xbrli:xbrl>";

        new StandaloneXbrlParser().Parse(xml).Should().BeEmpty();
    }
}
