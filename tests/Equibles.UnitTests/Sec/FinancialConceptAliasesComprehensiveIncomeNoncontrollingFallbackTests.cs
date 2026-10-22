using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Some filers tag comprehensive income only including noncontrolling interests;
/// without the fallback their Comprehensive Income line is blank for those years.
/// Per-period selection prefers lower-index refs, so the order is the contract:
/// the parent-attributable figure first, the NCI-inclusive one only after it.
/// </summary>
public class FinancialConceptAliasesComprehensiveIncomeNoncontrollingFallbackTests
{
    [Fact]
    public void TryResolve_ComprehensiveIncome_FallsBackToNoncontrollingInclusiveFigureLast()
    {
        FinancialConceptAliases.TryResolve("comprehensive-income", out var refs).Should().BeTrue();

        refs.Select(r => (r.Taxonomy, r.Tag))
            .Should()
            .Equal(
                (FactTaxonomy.UsGaap, "ComprehensiveIncomeNetOfTax"),
                (
                    FactTaxonomy.UsGaap,
                    "ComprehensiveIncomeNetOfTaxIncludingPortionAttributableToNoncontrollingInterest"
                )
            );
    }
}
