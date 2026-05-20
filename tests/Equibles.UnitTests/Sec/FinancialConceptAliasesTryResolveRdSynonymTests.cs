using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class FinancialConceptAliasesTryResolveRdSynonymTests
{
    // The class-level WHY-comment enumerates "r&d" as one of three natural-
    // phrasing examples callers must be able to use. `"r&d"` is not in Map —
    // it only lives in Synonyms — so resolution depends on the Synonyms→Map
    // hop firing AFTER Normalize lowercases the input. A refactor that drops
    // the synonym pass, reorders to Map-first only, or changes Normalize to
    // strip non-alphanumerics would silently break this exact phrasing with
    // no surviving test net (no other FinancialConceptAliases test exists).
    [Fact]
    public void TryResolve_AmpersandRd_ResolvesToResearchAndDevelopmentConcept()
    {
        var resolved = FinancialConceptAliases.TryResolve("R&D", out var concepts);

        resolved.Should().BeTrue();
        concepts.Should().ContainSingle();
        concepts[0].Taxonomy.Should().Be(FactTaxonomy.UsGaap);
        concepts[0].Tag.Should().Be("ResearchAndDevelopmentExpense");
    }
}
