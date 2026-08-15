using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Customer-concentration percentages are normally dimensioned by customer and
/// benchmark. Consolidated fact tools discard those dimensions, so advertising
/// this disclosure as a consolidated alias creates a guaranteed false miss.
/// </summary>
public class FinancialConceptAliasesCustomerConcentrationTests
{
    [Theory]
    [InlineData("customer-concentration")]
    [InlineData("concentration-risk")]
    [InlineData("customer-concentration-risk")]
    [InlineData("Customer Concentration")]
    public void TryResolve_CustomerConcentrationAliases_AreNotConsolidatedFacts(string alias)
    {
        var resolved = FinancialConceptAliases.TryResolve(alias, out var concepts);

        resolved.Should().BeFalse();
        concepts.Should().BeEmpty();
    }

    [Fact]
    public void SupportedAliases_DoNotListCustomerConcentration()
    {
        FinancialConceptAliases.SupportedAliases.Should().NotContain("customer-concentration");
    }
}
