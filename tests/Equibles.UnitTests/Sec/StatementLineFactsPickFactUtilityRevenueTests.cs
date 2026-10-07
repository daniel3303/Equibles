using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The statement Revenue line for utilities, which file their total as
/// <c>RegulatedAndUnregulatedOperatingRevenue</c>. Figures are FY2025 filings in
/// millions: a utility that stopped filing <c>Revenues</c> shows its total, a utility
/// whose contract tag is narrower shows the total, and a filer tagging only a slice
/// under the utility concept keeps its <c>Revenues</c> total.
/// </summary>
public class StatementLineFactsPickFactUtilityRevenueTests
{
    private const string Utility = "RegulatedAndUnregulatedOperatingRevenue";
    private const string Contract = "RevenueFromContractWithCustomerExcludingAssessedTax";

    private static StatementLine RevenueLine() =>
        FinancialStatementConcepts
            .For(FinancialStatementType.IncomeStatement)
            .Single(l => l.Alias == "revenue");

    private static FinancialFact Pick(params (string Tag, decimal Value)[] reported)
    {
        var conceptIdByKey = new Dictionary<(FactTaxonomy, string), Guid>();
        var facts = new Dictionary<Guid, FinancialFact>();
        foreach (var (tag, value) in reported)
        {
            var id = Guid.NewGuid();
            conceptIdByKey[(FactTaxonomy.UsGaap, tag)] = id;
            facts[id] = new FinancialFact { Value = value };
        }
        return StatementLineFacts.PickFact(RevenueLine(), conceptIdByKey, facts);
    }

    [Fact]
    public void PickFact_UtilityTotalOnly_RendersTheTotal()
    {
        // DTE files no Revenues or contract tag after 2018.
        Pick((Utility, 15_814m)).Value.Should().Be(15_814m);
    }

    [Fact]
    public void PickFact_UtilityTotalBesideNarrowerContractRevenue_PrefersTheTotal()
    {
        // NJR: contract revenue omits its non-ASC 606 energy-services revenue.
        Pick((Utility, 2_036m), (Contract, 1_351m)).Value.Should().Be(2_036m);
    }

    [Fact]
    public void PickFact_UtilitySliceBesideRevenues_KeepsRevenues()
    {
        // LNT tags only a slice under the utility concept.
        Pick(("Revenues", 4_362m), (Utility, 140m), (Contract, 4_362m)).Value.Should().Be(4_362m);
    }
}
