using Equibles.Sec.FinancialFacts.BusinessLogic.ReportedStatements;
using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.UnitTests.Sec;

public class ReportedStatementKindClassifierTests
{
    // Classification is by SEC's own role title. Order matters: comprehensive income must not be
    // swallowed by the plain-income arm, and an equity statement must not read as income.
    [Theory]
    [InlineData("CONSOLIDATED STATEMENTS OF OPERATIONS", ReportedStatementKind.Income)]
    [InlineData("CONSOLIDATED STATEMENTS OF INCOME", ReportedStatementKind.Income)]
    [InlineData(
        "CONSOLIDATED STATEMENTS OF COMPREHENSIVE INCOME",
        ReportedStatementKind.ComprehensiveIncome
    )]
    [InlineData("CONSOLIDATED BALANCE SHEETS", ReportedStatementKind.BalanceSheet)]
    [InlineData(
        "CONSOLIDATED STATEMENTS OF FINANCIAL POSITION",
        ReportedStatementKind.BalanceSheet
    )]
    [InlineData("CONSOLIDATED STATEMENTS OF CASH FLOWS", ReportedStatementKind.CashFlow)]
    [InlineData("CONSOLIDATED STATEMENTS OF SHAREHOLDERS' EQUITY", ReportedStatementKind.Equity)]
    [InlineData("CONSOLIDATED STATEMENTS OF STOCKHOLDERS' EQUITY", ReportedStatementKind.Equity)]
    [InlineData("Regulatory Capital Schedule", ReportedStatementKind.Other)]
    public void Classify_FromRoleTitle_PicksTheRightKind(
        string shortName,
        ReportedStatementKind expected
    )
    {
        ReportedStatementKindClassifier.Classify(shortName, null).Should().Be(expected);
    }

    [Theory]
    [InlineData("CONSOLIDATED BALANCE SHEETS (Parenthetical)", true)]
    [InlineData("CONSOLIDATED BALANCE SHEETS", false)]
    public void IsParenthetical_DetectsParentheticalCompanions(string shortName, bool expected)
    {
        ReportedStatementKindClassifier.IsParenthetical(shortName).Should().Be(expected);
    }
}
