using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Sibling to <c>FinancialConceptAliasesTryResolveOperatingProfitSynonymTests</c>.
/// "earnings" is the most semantically loaded synonym in the table — in
/// everyday finance the word covers EBITDA, EBIT, EPS, and net income; the
/// alias library makes the explicit contract decision that bare "earnings"
/// resolves to <c>NetIncomeLoss</c>, not EPS or operating income. A refactor
/// that re-routed "earnings" to <c>eps-basic</c> (a plausible "earnings per
/// share" misread) or to <c>operating-income</c> would compile cleanly, pass
/// every existing synonym pin (each targets a different alias), and silently
/// change MCP query semantics for every caller asking for "earnings".
/// </summary>
public class FinancialConceptAliasesTryResolveEarningsSynonymTests
{
    [Fact]
    public void TryResolve_EarningsAlias_RoutesToNetIncomeLossTag()
    {
        var matched = FinancialConceptAliases.TryResolve("earnings", out var concepts);

        matched.Should().BeTrue();
        var concept = concepts.Should().ContainSingle().Subject;
        concept.Taxonomy.Should().Be(FactTaxonomy.UsGaap);
        concept.Tag.Should().Be("NetIncomeLoss");
    }
}
