using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// <c>OperatingCostsAndExpenses</c> includes cost of revenue for some filers and
/// excludes it for others, so it must render as its own line: folded into
/// Total Operating Expenses or Total Costs &amp; Expenses it would mislabel one
/// group or the other. Without a line, a filer that tags only this concept shows
/// no cost total at all.
/// </summary>
public class FinancialStatementConceptsIncomeOperatingCostsAndExpensesTests
{
    [Fact]
    public void IncomeStatement_RendersOperatingCostsAndExpensesAsItsOwnLine()
    {
        var income = FinancialStatementConcepts.For(FinancialStatementType.IncomeStatement);
        var aliases = income.Select(l => l.Alias).ToList();

        var line = income
            .Should()
            .ContainSingle(l => l.Alias == "operating-costs-and-expenses")
            .Subject;
        line.Concepts.Select(r => (r.Taxonomy, r.Tag))
            .Should()
            .Equal((FactTaxonomy.UsGaap, "OperatingCostsAndExpenses"));
        aliases
            .IndexOf("operating-costs-and-expenses")
            .Should()
            .Be(aliases.IndexOf("total-costs-and-expenses") + 1);
    }

    [Theory]
    [InlineData("operating-expenses")]
    [InlineData("total-costs-and-expenses")]
    public void NeighbouringCostTotals_NeverFallBackToOperatingCostsAndExpenses(string alias)
    {
        FinancialConceptAliases.TryResolve(alias, out var refs).Should().BeTrue();

        refs.Should().NotContain(r => r.Tag == "OperatingCostsAndExpenses");
    }
}
