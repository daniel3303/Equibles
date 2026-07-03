using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.FinancialFacts.Data.Enums;

/// <summary>
/// The XBRL balance attribute of a concept — its normal accounting balance.
/// On an income statement a debit concept is expense-like and a credit concept
/// income-like; on a balance sheet debit means asset-like and credit means
/// liability/equity-like. Sourced from the filing's MetaLinks <c>crdr</c>
/// attribute; null for concepts without a balance (ratios, share counts, …).
/// </summary>
public enum ConceptBalance
{
    Debit = 1,
    Credit = 2,
}
