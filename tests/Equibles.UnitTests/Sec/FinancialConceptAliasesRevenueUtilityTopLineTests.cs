using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Utilities tag their income-statement total as
/// <c>RegulatedAndUnregulatedOperatingRevenue</c>; without it a utility that stopped
/// filing <c>Revenues</c> renders no revenue line. Per-period selection prefers
/// lower-index refs, so the order is the contract: after <c>Revenues</c> (some filers
/// tag a small slice here beside their total) and before the contract tags (a
/// utility's contract revenue omits alternative-revenue and derivative lines).
/// </summary>
public class FinancialConceptAliasesRevenueUtilityTopLineTests
{
    [Fact]
    public void TryResolve_Revenue_PlacesUtilityTotalBetweenHeadlineAndContractTags()
    {
        FinancialConceptAliases.TryResolve("revenue", out var refs).Should().BeTrue();

        var tags = refs.Select(r => r.Tag).ToList();
        var utilityIndex = tags.IndexOf("RegulatedAndUnregulatedOperatingRevenue");
        utilityIndex.Should().BePositive();
        refs[utilityIndex].Taxonomy.Should().Be(FactTaxonomy.UsGaap);
        utilityIndex.Should().BeGreaterThan(tags.IndexOf("Revenues"));
        utilityIndex.Should().BeGreaterThan(tags.IndexOf("RevenuesNetOfInterestExpense"));
        utilityIndex
            .Should()
            .BeLessThan(tags.IndexOf("RevenueFromContractWithCustomerExcludingAssessedTax"));
        utilityIndex
            .Should()
            .BeLessThan(tags.IndexOf("RevenueFromContractWithCustomerIncludingAssessedTax"));
    }
}
