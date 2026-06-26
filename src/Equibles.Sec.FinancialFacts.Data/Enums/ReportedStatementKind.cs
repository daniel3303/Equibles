using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.FinancialFacts.Data.Enums;

/// <summary>
/// Which financial statement an as-reported <see cref="Models.ReportedFinancialStatement"/>
/// represents, classified from the statement's role name in the filing's FilingSummary.xml
/// (SEC's own role titles). Drives the statement-type tabs on the Financials surfaces.
/// <see cref="Other"/> covers a "Statements"-category report that matches none of the named
/// kinds (rare — e.g. a regulatory capital schedule) so nothing is silently dropped.
/// </summary>
public enum ReportedStatementKind
{
    [Display(Name = "Income Statement")]
    Income = 0,

    [Display(Name = "Balance Sheet")]
    BalanceSheet = 1,

    [Display(Name = "Cash Flow")]
    CashFlow = 2,

    [Display(Name = "Stockholders' Equity")]
    Equity = 3,

    [Display(Name = "Comprehensive Income")]
    ComprehensiveIncome = 4,

    [Display(Name = "Other")]
    Other = 5,
}
