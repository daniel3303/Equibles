using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// Curated, ordered catalog mapping each <see cref="FinancialStatementType"/> to
/// the concepts that compose it, each line resolved through
/// <see cref="FinancialConceptAliases"/> so tag variants (concept transitions,
/// narrower per-industry tags) stay defined in exactly one place. Shared by the
/// Web Financials tab and the MCP GetFinancialStatement tool so both present
/// the same line items in the same order. Concepts a given company never
/// reports simply render no value — the catalog is intentionally broad rather
/// than per-company; renderers hide lines the company has never reported.
///
/// Placement note: this is curation policy, not data access, so it would
/// normally live in a *.BusinessLogic package. It sits in *.Data deliberately
/// because both the Web host and the MCP host must share one source of truth
/// and neither references the other; no FinancialFacts.BusinessLogic package
/// exists yet. Relocate there if/when one is introduced.
/// </summary>
public static class FinancialStatementConcepts
{
    // Resolves an alias to its ConceptRefs at catalog-construction time; an
    // unknown alias is a programming error, so it throws immediately rather
    // than rendering a permanently-empty line.
    private static StatementLine Line(string alias, string label)
    {
        if (!FinancialConceptAliases.TryResolve(alias, out var concepts) || concepts.Count == 0)
            throw new InvalidOperationException(
                $"Statement line '{label}' references unknown concept alias '{alias}'."
            );
        return new StatementLine(alias, label, concepts);
    }

    private static readonly IReadOnlyList<StatementLine> IncomeStatement =
    [
        Line("revenue", "Revenue"),
        // Financial-sector top lines. Banks, broker-dealers and insurers never file the
        // operating-company revenue tags above, so without these their income statement
        // shows no top line at all. Ordered as a financial income statement reads; a given
        // filer reports only the subset that fits it, and unreported lines simply don't render.
        Line("interest-and-dividend-income", "Interest & Dividend Income"),
        Line("net-interest-income", "Net Interest Income"),
        Line("noninterest-income", "Noninterest Income"),
        Line("total-net-revenue", "Total Net Revenue"),
        Line("premiums-earned", "Premiums Earned (Net)"),
        // REIT top-line detail: lessors report rental revenue under the lease-income
        // tags; renders alongside Revenue (the total) for real-estate filers only.
        Line("rental-income", "Rental Income"),
        Line("cost-of-revenue", "Cost of Revenue"),
        Line("gross-profit", "Gross Profit"),
        Line("research-and-development", "Research & Development"),
        Line("selling-and-marketing", "Selling & Marketing"),
        Line("general-and-administrative", "General & Administrative"),
        Line("selling-general-and-administrative", "Selling, General & Administrative"),
        Line("amortization-of-intangibles", "Amortization of Intangibles"),
        Line("restructuring", "Restructuring Charges"),
        Line("operating-expenses", "Total Operating Expenses"),
        Line("total-costs-and-expenses", "Total Costs & Expenses"),
        Line("operating-income", "Operating Income"),
        Line("interest-expense", "Interest Expense"),
        Line("interest-income", "Interest Income"),
        Line("other-nonoperating-income", "Other Non-Operating Income"),
        Line("pretax-income", "Pretax Income"),
        Line("income-tax", "Income Tax"),
        Line("net-income", "Net Income"),
        Line("comprehensive-income", "Comprehensive Income"),
        Line("eps-basic", "EPS (Basic)"),
        Line("eps-diluted", "EPS (Diluted)"),
        Line("weighted-average-shares-basic", "Shares Outstanding (Basic Avg)"),
        Line("weighted-average-shares-diluted", "Shares Outstanding (Diluted Avg)"),
    ];

    private static readonly IReadOnlyList<StatementLine> BalanceSheet =
    [
        Line("cash", "Cash & Cash Equivalents"),
        Line("short-term-investments", "Short-Term Investments"),
        Line("accounts-receivable", "Accounts Receivable"),
        Line("inventory", "Inventory"),
        Line("current-assets", "Total Current Assets"),
        Line("property-plant-and-equipment", "Property, Plant & Equipment (Net)"),
        Line("operating-lease-assets", "Operating Lease Assets"),
        Line("goodwill", "Goodwill"),
        Line("intangible-assets", "Intangible Assets (Net)"),
        Line("long-term-investments", "Long-Term Investments"),
        Line("total-assets", "Total Assets"),
        Line("accounts-payable", "Accounts Payable"),
        Line("accrued-liabilities", "Accrued Liabilities"),
        Line("deferred-revenue", "Deferred Revenue (Current)"),
        Line("short-term-debt", "Short-Term Debt"),
        Line("current-liabilities", "Total Current Liabilities"),
        Line("long-term-debt", "Long-Term Debt"),
        Line("operating-lease-liabilities", "Operating Lease Liabilities"),
        Line("deferred-revenue-noncurrent", "Deferred Revenue (Non-Current)"),
        Line("total-liabilities", "Total Liabilities"),
        Line("common-stock-value", "Common Stock"),
        Line("additional-paid-in-capital", "Additional Paid-In Capital"),
        Line("retained-earnings", "Retained Earnings"),
        Line("accumulated-oci", "Accumulated Other Comprehensive Income"),
        Line("treasury-stock", "Treasury Stock"),
        Line("stockholders-equity", "Total Stockholders' Equity"),
        Line("total-liabilities-and-equity", "Total Liabilities & Equity"),
    ];

    private static readonly IReadOnlyList<StatementLine> CashFlow =
    [
        Line("net-income", "Net Income"),
        Line("depreciation-and-amortization", "Depreciation & Amortization"),
        Line("share-based-compensation", "Share-Based Compensation"),
        Line("deferred-income-taxes", "Deferred Income Taxes"),
        Line("operating-cash-flow", "Operating Cash Flow"),
        Line("capital-expenditures", "Capital Expenditures"),
        Line("acquisitions", "Acquisitions (Net of Cash)"),
        Line("investing-cash-flow", "Investing Cash Flow"),
        Line("share-repurchases", "Share Repurchases"),
        Line("dividends-paid", "Dividends Paid"),
        Line("debt-issued", "Debt Issued"),
        Line("debt-repaid", "Debt Repaid"),
        Line("stock-issued", "Common Stock Issued"),
        Line("financing-cash-flow", "Financing Cash Flow"),
        Line("fx-effect-on-cash", "Exchange-Rate Effect on Cash"),
        Line("net-change-in-cash", "Net Change in Cash"),
    ];

    public static IReadOnlyList<StatementLine> For(FinancialStatementType type) =>
        type switch
        {
            FinancialStatementType.IncomeStatement => IncomeStatement,
            FinancialStatementType.BalanceSheet => BalanceSheet,
            FinancialStatementType.CashFlow => CashFlow,
            _ => [],
        };
}
