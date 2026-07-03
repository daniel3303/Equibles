using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Redundant share-count tag vintages fold into their canonical alias instead of
/// listing as separate near-duplicate metrics. The preferred tag leads; the
/// vintage sits LAST so per-period selection only reaches for it where the
/// preferred tag is absent (companies that never split basic from diluted, or
/// only file the balance-sheet share count).
/// </summary>
public class FinancialConceptAliasesShareCountMergeTests
{
    [Fact]
    public void TryResolve_WeightedAverageSharesBasic_FallsBackToCombinedTagLast()
    {
        FinancialConceptAliases
            .TryResolve("weighted-average-shares-basic", out var refs)
            .Should()
            .BeTrue();

        refs[0].Tag.Should().Be("WeightedAverageNumberOfSharesOutstandingBasic");
        refs[^1].Tag.Should().Be("WeightedAverageNumberOfShareOutstandingBasicAndDiluted");
    }

    [Fact]
    public void TryResolve_WeightedAverageSharesDiluted_FallsBackToCombinedTagLast()
    {
        FinancialConceptAliases
            .TryResolve("weighted-average-shares-diluted", out var refs)
            .Should()
            .BeTrue();

        refs[0].Tag.Should().Be("WeightedAverageNumberOfDilutedSharesOutstanding");
        refs[^1].Tag.Should().Be("WeightedAverageNumberOfShareOutstandingBasicAndDiluted");
    }

    [Fact]
    public void TryResolve_SharesOutstanding_PrefersCoverPageThenBalanceSheetTag()
    {
        FinancialConceptAliases.TryResolve("shares-outstanding", out var refs).Should().BeTrue();

        // The dei cover-page count (as of the filing date) leads.
        refs[0].Taxonomy.Should().Be(FactTaxonomy.Dei);
        refs[0].Tag.Should().Be("EntityCommonStockSharesOutstanding");
        // The us-gaap balance-sheet count fills periods the cover-page tag lacks.
        refs[^1].Taxonomy.Should().Be(FactTaxonomy.UsGaap);
        refs[^1].Tag.Should().Be("CommonStockSharesOutstanding");
    }
}
