using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class FinancialConceptAliasesTryResolveUnderscoreSeparatorTests
{
    [Fact]
    public void TryResolve_AliasWithUnderscoreSeparator_NormalizesToDashAndResolves()
    {
        // Sibling to the SlashSeparator pin. Normalize folds '_' → '-' so
        // callers can use snake_case ("operating_income") interchangeably with
        // kebab-case ("operating-income"). MCP tool callers routinely emit
        // identifier-style names (most LLMs default to snake_case when asked
        // for a "concept name"). A refactor pruning Replace('_', '-') on the
        // false intuition that "we already accept dashes" would compile, pass
        // every existing alias test, and silently break every snake_case
        // query — the user sees "no match" with no error to chase.
        var success = FinancialConceptAliases.TryResolve("operating_income", out var concepts);

        success.Should().BeTrue();
        concepts.Should().HaveCount(1);
        concepts[0].Taxonomy.Should().Be(FactTaxonomy.UsGaap);
        concepts[0].Tag.Should().Be("OperatingIncomeLoss");
    }
}
