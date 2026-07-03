using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The revenue alias must reach back past the ASC 606 transition: large filers
/// (AAPL, MSFT) reported total revenue as <c>SalesRevenueNet</c> through
/// ~FY2017, so a series built from the modern tags alone starts mid-history.
/// The legacy tag sits AFTER the modern tags so it only fills periods they
/// lack — per-period selection prefers lower-index refs.
/// </summary>
public class FinancialConceptAliasesRevenuePreAsc606Tests
{
    [Fact]
    public void TryResolve_Revenue_IncludesLegacySalesRevenueNetAfterModernTags()
    {
        FinancialConceptAliases.TryResolve("revenue", out var refs).Should().BeTrue();

        refs[0].Tag.Should().Be("Revenues");
        var tags = refs.Select(r => r.Tag).ToList();
        var legacyIndex = tags.IndexOf("SalesRevenueNet");
        legacyIndex.Should().BePositive();
        legacyIndex
            .Should()
            .BeGreaterThan(tags.IndexOf("RevenueFromContractWithCustomerExcludingAssessedTax"));
        refs[legacyIndex].Taxonomy.Should().Be(FactTaxonomy.UsGaap);
    }
}
