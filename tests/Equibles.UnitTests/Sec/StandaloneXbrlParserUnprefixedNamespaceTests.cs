using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the taxonomy-prefix contract of StandaloneXbrlParser.Parse: a numeric
/// fact element whose namespace has no xmlns:prefix binding in scope cannot be
/// classified to a taxonomy (us-gaap, ifrs-full, dei, …) and must be skipped
/// rather than emitted with an empty Taxonomy. Downstream FinancialFacts
/// tools key off Taxonomy to disambiguate same-tag concepts across taxonomies;
/// a fact with empty Taxonomy would silently merge into the wrong concept.
/// </summary>
public class StandaloneXbrlParserUnprefixedNamespaceTests
{
    [Fact]
    public void Parse_FactInDefaultUnprefixedNamespace_SkipsFact()
    {
        // The fact element is in a custom namespace declared as the default
        // (xmlns="…", no prefix). System.Xml.Linq's GetPrefixOfNamespace
        // returns null for namespaces bound only as the default — the parser
        // must drop the fact instead of emitting Taxonomy = "" or null.
        var xml =
            "<xbrli:xbrl "
            + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\">"
            + "<xbrli:context id=\"C1\">"
            + "<xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "<xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "<Revenues xmlns=\"http://example.com/customns\" contextRef=\"C1\" unitRef=\"u\">100</Revenues>"
            + "</xbrli:xbrl>";

        new StandaloneXbrlParser().Parse(xml).Should().BeEmpty();
    }
}
