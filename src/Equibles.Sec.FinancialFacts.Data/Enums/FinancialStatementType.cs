using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.FinancialFacts.Data.Enums;

/// <summary>
/// The three core financial statements assembled from individual
/// <see cref="Models.FinancialFact"/> rows. Each maps to a curated, ordered set
/// of us-gaap concepts (see <see cref="Statements.FinancialStatementConcepts"/>).
/// Company-specific dimensional facts (e.g. product-segment revenue) are out of
/// scope here and handled separately.
/// </summary>
public enum FinancialStatementType
{
    [Display(Name = "Income Statement")]
    IncomeStatement,

    [Display(Name = "Balance Sheet")]
    BalanceSheet,

    [Display(Name = "Cash Flow")]
    CashFlow,
}
