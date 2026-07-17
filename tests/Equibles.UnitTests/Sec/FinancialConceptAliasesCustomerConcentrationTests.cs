using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Filers that tag their customer-concentration disclosure carry it under the
/// ConcentrationRiskPercentage elements (us-gaap and srt spellings). The
/// 'customer-concentration' alias makes that tagged data reachable through
/// the FinancialFacts tools; without it the tags were ingested but had no
/// caller-facing name.
/// </summary>
public class FinancialConceptAliasesCustomerConcentrationTests
{
    [Fact]
    public void TryResolve_CustomerConcentration_MapsConcentrationRiskPercentageTags()
    {
        var resolved = FinancialConceptAliases.TryResolve(
            "customer-concentration",
            out var concepts
        );

        resolved.Should().BeTrue();
        concepts
            .Select(c => (c.Taxonomy, c.Tag))
            .Should()
            .ContainInOrder(
                (FactTaxonomy.UsGaap, "ConcentrationRiskPercentage1"),
                (FactTaxonomy.UsGaap, "ConcentrationRiskPercentage"),
                (FactTaxonomy.Srt, "ConcentrationRiskPercentage1"),
                (FactTaxonomy.Srt, "ConcentrationRiskPercentage")
            );
    }

    [Theory]
    [InlineData("concentration-risk")]
    [InlineData("customer-concentration-risk")]
    [InlineData("Customer Concentration")]
    public void TryResolve_Synonyms_ResolveToCustomerConcentration(string alias)
    {
        var resolved = FinancialConceptAliases.TryResolve(alias, out var concepts);

        resolved.Should().BeTrue();
        concepts.Should().Contain(c => c.Tag == "ConcentrationRiskPercentage1");
    }

    [Fact]
    public void SupportedAliases_ListCustomerConcentration()
    {
        FinancialConceptAliases.SupportedAliases.Should().Contain("customer-concentration");
    }
}
