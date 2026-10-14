using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class FinancialConceptAliasesIsInstantTests
{
    // The statement catalog is an independent classification: every balance-sheet line is a
    // position as of a date, every income and cash-flow line a flow over a period.
    [Fact]
    public void IsInstant_AgreesWithTheStatementCatalog()
    {
        FinancialStatementConcepts
            .For(FinancialStatementType.BalanceSheet)
            .Should()
            .OnlyContain(line => FinancialConceptAliases.IsInstant(line.Alias));
        FinancialStatementConcepts
            .For(FinancialStatementType.IncomeStatement)
            .Concat(FinancialStatementConcepts.For(FinancialStatementType.CashFlow))
            .Should()
            .NotContain(line => FinancialConceptAliases.IsInstant(line.Alias));
    }

    [Theory]
    [InlineData("shares-outstanding", true)]
    [InlineData("Total Assets", true)]
    [InlineData("equity", true)]
    [InlineData("weighted-average-shares-diluted", false)]
    [InlineData("revenue", false)]
    [InlineData("unknown-alias", false)]
    public void IsInstant_ResolvesSynonymsBeforeClassifying(string alias, bool expected)
    {
        FinancialConceptAliases.IsInstant(alias).Should().Be(expected);
    }
}
