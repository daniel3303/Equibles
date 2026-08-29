using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class FinancialConceptAliasesIfrsTests
{
    public static TheoryData<string, string> CoreAliases =>
        new()
        {
            { "revenue", "Revenue" },
            { "gross-profit", "GrossProfit" },
            { "operating-income", "ProfitLossFromOperatingActivities" },
            { "pretax-income", "ProfitLossBeforeTax" },
            { "net-income", "ProfitLossAttributableToOwnersOfParent" },
            { "net-income", "ProfitLoss" },
            { "eps-diluted", "DilutedEarningsLossPerShare" },
            { "cash", "CashAndCashEquivalents" },
            { "current-assets", "CurrentAssets" },
            { "total-assets", "Assets" },
            { "current-liabilities", "CurrentLiabilities" },
            { "total-liabilities", "Liabilities" },
            { "stockholders-equity", "EquityAttributableToOwnersOfParent" },
            { "operating-cash-flow", "CashFlowsFromUsedInOperatingActivities" },
            {
                "capital-expenditures",
                "PurchaseOfPropertyPlantAndEquipmentClassifiedAsInvestingActivities"
            },
            { "dividends-paid", "DividendsPaid" },
            {
                "current-financial-assets-fvtpl",
                "CurrentFinancialAssetsAtFairValueThroughProfitOrLoss"
            },
            {
                "current-financial-assets-fvoci",
                "CurrentFinancialAssetsAtFairValueThroughOtherComprehensiveIncome"
            },
            { "current-financial-assets-amortised-cost", "CurrentFinancialAssetsAtAmortisedCost" },
        };

    [Theory]
    [MemberData(nameof(CoreAliases))]
    public void TryResolve_CoreAlias_IncludesIfrsConcept(string alias, string tag)
    {
        FinancialConceptAliases.TryResolve(alias, out var refs).Should().BeTrue();

        refs.Should().Contain(r => r.Taxonomy == FactTaxonomy.IfrsFull && r.Tag == tag);
    }

    [Fact]
    public void NetIncome_OrdersParentAttributableConceptsBeforeGenericProfitLossFallbacks()
    {
        FinancialConceptAliases.TryResolve("net-income", out var refs).Should().BeTrue();

        refs.Select(reference => (reference.Taxonomy, reference.Tag))
            .Should()
            .Equal(
                (FactTaxonomy.UsGaap, "NetIncomeLoss"),
                (FactTaxonomy.IfrsFull, "ProfitLossAttributableToOwnersOfParent"),
                (FactTaxonomy.UsGaap, "ProfitLoss"),
                (FactTaxonomy.IfrsFull, "ProfitLoss")
            );
    }

    [Fact]
    public void DividendsPaid_OrdersCashFlowConceptBeforeGenericEquityMovement()
    {
        FinancialConceptAliases.TryResolve("dividends-paid", out var refs).Should().BeTrue();

        var ifrs = refs.Where(reference => reference.Taxonomy == FactTaxonomy.IfrsFull).ToList();
        ifrs.Select(reference => reference.Tag)
            .Should()
            .Equal("DividendsPaidClassifiedAsFinancingActivities", "DividendsPaid");
    }
}
