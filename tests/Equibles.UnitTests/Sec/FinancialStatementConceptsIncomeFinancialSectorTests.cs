using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class FinancialStatementConceptsIncomeFinancialSectorTests
{
    // Banks, broker-dealers and insurers never file the operating-company revenue tags
    // (Revenues / RevenueFromContractWithCustomerExcludingAssessedTax), so without their
    // sector top-line concepts the income statement renders no top line at all — the
    // original defect, where a bank's card collapsed to Operating Income + Net Income + EPS.
    // Pin that the shared catalog carries the financial-sector top lines so a future trim
    // can't silently re-break every financial company on all four surfaces that share it.
    [Theory]
    [InlineData("RevenuesNetOfInterestExpense")]
    [InlineData("InterestIncomeExpenseNet")]
    [InlineData("InterestAndDividendIncomeOperating")]
    [InlineData("NoninterestIncome")]
    [InlineData("PremiumsEarnedNet")]
    public void IncomeStatement_IncludesFinancialSectorTopLine(string tag)
    {
        var income = FinancialStatementConcepts.For(FinancialStatementType.IncomeStatement);

        income.Should().Contain(line => line.Tag == tag && line.Taxonomy == FactTaxonomy.UsGaap);
    }
}
