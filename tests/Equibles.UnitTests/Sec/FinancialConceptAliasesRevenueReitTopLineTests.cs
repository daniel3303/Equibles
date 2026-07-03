using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// REITs never file the operating-company revenue tags the alias preferred:
/// ARE tags its total-revenue line with the assessed-tax-inclusive ASC 606
/// variant (2016+) and filed <c>RealEstateRevenueNet</c> before that — without
/// both, a REIT's income statement renders no Revenue row at all. The
/// inclusive variant must stay behind the excluding form so filers reporting
/// both keep the cleaner measure per period.
/// </summary>
public class FinancialConceptAliasesRevenueReitTopLineTests
{
    [Fact]
    public void TryResolve_Revenue_CoversReitTopLineTags()
    {
        FinancialConceptAliases.TryResolve("revenue", out var refs).Should().BeTrue();

        var tags = refs.Select(r => r.Tag).ToList();
        tags.Should().Contain("RevenueFromContractWithCustomerIncludingAssessedTax");
        tags.Should().Contain("RealEstateRevenueNet");
        tags.IndexOf("RevenueFromContractWithCustomerIncludingAssessedTax")
            .Should()
            .BeGreaterThan(tags.IndexOf("RevenueFromContractWithCustomerExcludingAssessedTax"));
    }

    [Fact]
    public void TryResolve_RentalIncome_PrefersBroadLeaseIncomeTotal()
    {
        FinancialConceptAliases.TryResolve("rental-income", out var refs).Should().BeTrue();

        var tags = refs.Select(r => r.Tag).ToList();
        // LeaseIncome (operating + sales-type/direct-financing) leads so the
        // operating-only component never shadows a reported total.
        tags[0].Should().Be("LeaseIncome");
        tags.Should().Contain("OperatingLeaseLeaseIncome");
        tags.Should().Contain("OperatingLeasesIncomeStatementLeaseRevenue");
    }
}
