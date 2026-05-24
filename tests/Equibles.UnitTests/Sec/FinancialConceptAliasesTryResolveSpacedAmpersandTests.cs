using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class FinancialConceptAliasesTryResolveSpacedAmpersandTests
{
    // Contract: "callers can use natural phrasing ('r&d')". Normalize converts
    // spaces to hyphens, so "R & D" becomes "r-&-d" — which doesn't match the
    // synonym key "r&d". Spaced ampersand is common in financial prose and MCP
    // tool input; the normalization should not break the synonym lookup.
    [Fact]
    public void TryResolve_SpacedAmpersandRd_ResolvesToResearchAndDevelopment()
    {
        var resolved = FinancialConceptAliases.TryResolve("R & D", out var concepts);

        resolved.Should().BeTrue();
        concepts.Should().ContainSingle();
        concepts[0].Taxonomy.Should().Be(FactTaxonomy.UsGaap);
        concepts[0].Tag.Should().Be("ResearchAndDevelopmentExpense");
    }
}
