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
        };

    [Theory]
    [MemberData(nameof(CoreAliases))]
    public void TryResolve_CoreAlias_IncludesIfrsConcept(string alias, string tag)
    {
        FinancialConceptAliases.TryResolve(alias, out var refs).Should().BeTrue();

        refs.Should().Contain(r => r.Taxonomy == FactTaxonomy.IfrsFull && r.Tag == tag);
    }
}
