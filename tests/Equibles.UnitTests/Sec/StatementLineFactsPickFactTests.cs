using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The shared variant-selection rule every statement surface uses: the FIRST
/// variant (declaration order) with a fact wins, later variants only fill the
/// gap — so a company reporting both the broad and the narrow tag always shows
/// the broad one, and a company reporting only the narrow variant (ADBE's
/// software R&amp;D) still renders a value instead of a dash.
/// </summary>
public class StatementLineFactsPickFactTests
{
    private static StatementLine RdLine() =>
        FinancialStatementConcepts
            .For(FinancialStatementType.IncomeStatement)
            .Single(l => l.Alias == "research-and-development");

    private static FinancialFact Fact(decimal value) => new() { Value = value };

    [Fact]
    public void PickFact_PreferredTagReported_WinsOverVariant()
    {
        var line = RdLine();
        var genericId = Guid.NewGuid();
        var softwareId = Guid.NewGuid();
        var conceptIdByKey = new Dictionary<(FactTaxonomy, string), Guid>
        {
            [(FactTaxonomy.UsGaap, "ResearchAndDevelopmentExpense")] = genericId,
            [
                (
                    FactTaxonomy.UsGaap,
                    "ResearchAndDevelopmentExpenseSoftwareExcludingAcquiredInProcessCost"
                )
            ] = softwareId,
        };
        var facts = new Dictionary<Guid, FinancialFact>
        {
            [genericId] = Fact(100),
            [softwareId] = Fact(200),
        };

        var picked = StatementLineFacts.PickFact(line, conceptIdByKey, facts);

        picked.Value.Should().Be(100);
    }

    [Fact]
    public void PickFact_OnlyVariantReported_FillsFromVariant()
    {
        var line = RdLine();
        var softwareId = Guid.NewGuid();
        var conceptIdByKey = new Dictionary<(FactTaxonomy, string), Guid>
        {
            [
                (
                    FactTaxonomy.UsGaap,
                    "ResearchAndDevelopmentExpenseSoftwareExcludingAcquiredInProcessCost"
                )
            ] = softwareId,
        };
        var facts = new Dictionary<Guid, FinancialFact> { [softwareId] = Fact(200) };

        var picked = StatementLineFacts.PickFact(line, conceptIdByKey, facts);

        picked.Value.Should().Be(200);
    }

    [Fact]
    public void PickFact_NothingReported_ReturnsNull()
    {
        var line = RdLine();

        var picked = StatementLineFacts.PickFact(
            line,
            new Dictionary<(FactTaxonomy, string), Guid>(),
            new Dictionary<Guid, FinancialFact>()
        );

        picked.Should().BeNull();
    }

    [Fact]
    public void CollectConceptPairs_ReturnsEveryVariantTag()
    {
        var (taxonomies, tags) = StatementLineFacts.CollectConceptPairs([RdLine()]);

        taxonomies.Should().Equal(FactTaxonomy.UsGaap);
        tags.Should()
            .Contain([
                "ResearchAndDevelopmentExpense",
                "ResearchAndDevelopmentExpenseSoftwareExcludingAcquiredInProcessCost",
            ]);
    }
}
