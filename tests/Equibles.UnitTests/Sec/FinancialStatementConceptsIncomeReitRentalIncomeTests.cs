using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class FinancialStatementConceptsIncomeReitRentalIncomeTests
{
    // REITs report rental revenue under the lease-income tags, which no
    // operating-company or financial-sector line resolves — without this line
    // a lessor's income statement shows no rental detail. Pin the catalog so a
    // future trim can't silently drop the REIT top-line block.
    [Theory]
    [InlineData("LeaseIncome")]
    [InlineData("OperatingLeaseLeaseIncome")]
    [InlineData("OperatingLeasesIncomeStatementLeaseRevenue")]
    public void IncomeStatement_IncludesReitRentalIncomeVariant(string tag)
    {
        var income = FinancialStatementConcepts.For(FinancialStatementType.IncomeStatement);

        var line = income.Should().ContainSingle(l => l.Alias == "rental-income").Subject;
        line.Concepts.Should().Contain(r => r.Tag == tag && r.Taxonomy == FactTaxonomy.UsGaap);
    }
}
