using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// Curated, ordered catalog mapping each <see cref="FinancialStatementType"/> to
/// the us-gaap concepts that compose it. Shared by the Web Financials tab and
/// the MCP GetFinancialStatement tool so both present the same line items in the
/// same order. Concepts a given company never reports simply render no value —
/// the catalog is intentionally broad rather than per-company.
///
/// Placement note: this is curation policy, not data access, so it would
/// normally live in a *.BusinessLogic package. It sits in *.Data deliberately
/// because both the Web host and the MCP host must share one source of truth
/// and neither references the other; no FinancialFacts.BusinessLogic package
/// exists yet. Relocate there if/when one is introduced.
/// </summary>
public static class FinancialStatementConcepts
{
    private static readonly IReadOnlyList<StatementLine> IncomeStatement =
    [
        new(FactTaxonomy.UsGaap, "Revenues", "Revenue"),
        new(
            FactTaxonomy.UsGaap,
            "RevenueFromContractWithCustomerExcludingAssessedTax",
            "Revenue (ASC 606)"
        ),
        // Financial-sector top lines. Banks, broker-dealers and insurers never file the
        // operating-company revenue tags above, so without these their income statement
        // shows no top line at all. Ordered as a financial income statement reads; a given
        // filer reports only the subset that fits it, and unreported lines simply don't render.
        new(
            FactTaxonomy.UsGaap,
            "InterestAndDividendIncomeOperating",
            "Interest & Dividend Income"
        ),
        new(FactTaxonomy.UsGaap, "InterestIncomeExpenseNet", "Net Interest Income"),
        new(FactTaxonomy.UsGaap, "NoninterestIncome", "Noninterest Income"),
        new(FactTaxonomy.UsGaap, "RevenuesNetOfInterestExpense", "Total Net Revenue"),
        new(FactTaxonomy.UsGaap, "PremiumsEarnedNet", "Premiums Earned (Net)"),
        new(FactTaxonomy.UsGaap, "CostOfRevenue", "Cost of Revenue"),
        new(FactTaxonomy.UsGaap, "GrossProfit", "Gross Profit"),
        new(FactTaxonomy.UsGaap, "OperatingExpenses", "Operating Expenses"),
        new(FactTaxonomy.UsGaap, "ResearchAndDevelopmentExpense", "Research & Development"),
        new(FactTaxonomy.UsGaap, "OperatingIncomeLoss", "Operating Income"),
        new(FactTaxonomy.UsGaap, "NetIncomeLoss", "Net Income"),
        new(FactTaxonomy.UsGaap, "EarningsPerShareBasic", "EPS (Basic)"),
        new(FactTaxonomy.UsGaap, "EarningsPerShareDiluted", "EPS (Diluted)"),
    ];

    private static readonly IReadOnlyList<StatementLine> BalanceSheet =
    [
        new(
            FactTaxonomy.UsGaap,
            "CashAndCashEquivalentsAtCarryingValue",
            "Cash & Cash Equivalents"
        ),
        new(FactTaxonomy.UsGaap, "AssetsCurrent", "Total Current Assets"),
        new(FactTaxonomy.UsGaap, "Assets", "Total Assets"),
        new(FactTaxonomy.UsGaap, "LiabilitiesCurrent", "Total Current Liabilities"),
        new(FactTaxonomy.UsGaap, "Liabilities", "Total Liabilities"),
        new(FactTaxonomy.UsGaap, "RetainedEarningsAccumulatedDeficit", "Retained Earnings"),
        new(FactTaxonomy.UsGaap, "StockholdersEquity", "Total Stockholders' Equity"),
    ];

    private static readonly IReadOnlyList<StatementLine> CashFlow =
    [
        new(
            FactTaxonomy.UsGaap,
            "NetCashProvidedByUsedInOperatingActivities",
            "Operating Cash Flow"
        ),
        new(
            FactTaxonomy.UsGaap,
            "NetCashProvidedByUsedInInvestingActivities",
            "Investing Cash Flow"
        ),
        new(
            FactTaxonomy.UsGaap,
            "NetCashProvidedByUsedInFinancingActivities",
            "Financing Cash Flow"
        ),
        new(
            FactTaxonomy.UsGaap,
            "CashAndCashEquivalentsPeriodIncreaseDecrease",
            "Net Change in Cash"
        ),
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
