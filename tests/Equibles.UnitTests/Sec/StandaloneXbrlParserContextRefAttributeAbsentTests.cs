using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Adversarial sibling to <c>Parse_ContextRefMissing_SkipsFact</c>, whose XML
/// fixture actually exercises a *dangling* contextRef (`contextRef="DoesNotExist"`
/// — attribute present, value unmatched by the contexts map). The truly absent
/// case — a numeric fact element with no <c>contextRef</c> attribute at all —
/// trips a different guard: the early <c>string.IsNullOrEmpty(contextRef)</c>
/// check before the contexts-map lookup. Without that first guard, the absent
/// attribute coerces to <c>null</c>, and <c>Dictionary&lt;string,_&gt;.TryGetValue(null,…)</c>
/// throws <see cref="ArgumentNullException"/> — the parser would crash instead
/// of silently skipping the malformed fact. The contract derived from the
/// function name <c>TryParseFact</c> is "drop, never throw" on any fact whose
/// referential integrity can't be resolved.
/// </summary>
public class StandaloneXbrlParserContextRefAttributeAbsentTests
{
    [Fact]
    public void Parse_FactWithNoContextRefAttribute_SkipsFactWithoutThrowing()
    {
        var xml =
            "<xbrli:xbrl "
            + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
            + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\">"
            + "<xbrli:context id=\"C1\">"
            + "<xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "<xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "<us-gaap:Revenues unitRef=\"u\">100</us-gaap:Revenues>"
            + "</xbrli:xbrl>";

        var act = () => new StandaloneXbrlParser().Parse(xml);

        var facts = act.Should().NotThrow().Subject;
        facts.Should().BeEmpty();
    }
}
