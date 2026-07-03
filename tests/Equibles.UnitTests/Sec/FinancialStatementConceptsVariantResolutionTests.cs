using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Statement lines resolve through FinancialConceptAliases and carry ordered
/// tag variants. ADBE files R&amp;D under the software-specific tag and never
/// the generic one, so its Financials tab rendered a permanent dash until the
/// line learned the variant — pin that the variant stays, ordered after the
/// generic (preferred) tag so per-period selection favours the broad measure.
/// </summary>
public class FinancialStatementConceptsVariantResolutionTests
{
    [Fact]
    public void IncomeStatement_ResearchAndDevelopment_CarriesSoftwareVariantAfterGenericTag()
    {
        var income = FinancialStatementConcepts.For(FinancialStatementType.IncomeStatement);
        var line = income.Single(l => l.Alias == "research-and-development");

        var tags = line.Concepts.Select(c => c.Tag).ToList();
        tags[0].Should().Be("ResearchAndDevelopmentExpense");
        tags.Should()
            .Contain("ResearchAndDevelopmentExpenseSoftwareExcludingAcquiredInProcessCost");
    }

    [Fact]
    public void AllStatementLines_ResolveToAtLeastOneConcept()
    {
        foreach (var type in Enum.GetValues<FinancialStatementType>())
        {
            foreach (var line in FinancialStatementConcepts.For(type))
            {
                line.Concepts.Should()
                    .NotBeEmpty($"line '{line.Label}' ({type}) must resolve its alias");
                line.Tag.Should().Be(line.Concepts[0].Tag);
            }
        }
    }

    [Fact]
    public void CashFlow_NetChangeInCash_PrefersModernRestrictedCashTag()
    {
        var cashFlow = FinancialStatementConcepts.For(FinancialStatementType.CashFlow);
        var line = cashFlow.Single(l => l.Alias == "net-change-in-cash");

        // The pre-2018 tag must stay a fallback only: it was deprecated with
        // ASU 2016-18 and modern filers only report the restricted-cash shape.
        line.Tag.Should()
            .Be(
                "CashCashEquivalentsRestrictedCashAndRestrictedCashEquivalentsPeriodIncreaseDecreaseIncludingExchangeRateEffect"
            );
        line.Concepts.Select(c => c.Tag)
            .Should()
            .Contain("CashAndCashEquivalentsPeriodIncreaseDecrease");
    }
}
