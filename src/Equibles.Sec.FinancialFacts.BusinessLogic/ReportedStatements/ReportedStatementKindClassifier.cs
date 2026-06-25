using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.ReportedStatements;

/// <summary>
/// Classifies a statement into a <see cref="ReportedStatementKind"/> tab from its FilingSummary
/// role title — SEC's own role names, not a heuristic on values. Order matters: more specific
/// titles are matched first (comprehensive income before plain income; equity before income).
/// </summary>
public static class ReportedStatementKindClassifier
{
    public static ReportedStatementKind Classify(string shortName, string longName)
    {
        var text = (
            (shortName ?? string.Empty) + " " + (longName ?? string.Empty)
        ).ToUpperInvariant();

        if (text.Contains("COMPREHENSIVE INCOME") || text.Contains("COMPREHENSIVE LOSS"))
        {
            return ReportedStatementKind.ComprehensiveIncome;
        }
        if (text.Contains("CASH FLOW"))
        {
            return ReportedStatementKind.CashFlow;
        }
        if (
            text.Contains("STOCKHOLDERS")
            || text.Contains("SHAREHOLDERS")
            || text.Contains("CHANGES IN EQUITY")
            || text.Contains("STATEMENT OF EQUITY")
            || text.Contains("STATEMENTS OF EQUITY")
        )
        {
            return ReportedStatementKind.Equity;
        }
        if (
            text.Contains("BALANCE SHEET")
            || text.Contains("FINANCIAL POSITION")
            || text.Contains("FINANCIAL CONDITION")
        )
        {
            return ReportedStatementKind.BalanceSheet;
        }
        if (
            text.Contains("OPERATIONS")
            || text.Contains("INCOME")
            || text.Contains("EARNINGS")
            || text.Contains("LOSS")
        )
        {
            return ReportedStatementKind.Income;
        }
        return ReportedStatementKind.Other;
    }

    /// <summary>A parenthetical companion statement (per-share / shares-authorized detail), from the role title.</summary>
    public static bool IsParenthetical(string shortName) =>
        (shortName ?? string.Empty).Contains("Parenthetical", StringComparison.OrdinalIgnoreCase);
}
