namespace Equibles.TestSupport;

/// <summary>
/// CareTrust REIT's earnings release of 5 November 2026 (thousands), with the lines of its
/// third quarter and nine months of 2025 and of its 2025 year-end balance sheet as its 10-Q
/// and 10-K reported them: tag, the label on the statement's face, and the figures the release
/// repeats as its comparative columns.
/// </summary>
public static class CareTrustRelease
{
    public const string IncomeStatement = """
        | CARETRUST REIT, INC. |  |  |  |  |  |
        | --- | --- | --- | --- | --- | --- |
        | CONSOLIDATED INCOME STATEMENTS |  |  |  |  |  |
        | (in thousands, except per share data) |  |  |  |  |  |
        | (Unaudited) |  |  |  |  |  |
        |  |  | For the Three Months EndedSeptember 30, |  | For the Nine Months EndedSeptember 30, |  |
        |  |  | 2026 | 2025 | 2026 | 2025 |
        | Revenues: |  |  |  |  |  |
        |  | Rental income | 127,236 | 104,265 | 359,637 | 261,944 |
        |  | Resident fees and services | 7,226 | — | 15,721 | — |
        |  | Interest income from financing receivables | 14,030 | 2,908 | 28,504 | 8,601 |
        |  | Interest income from other real estate related investments and other income | 27,868 | 25,271 | 76,629 | 70,989 |
        |  | Total revenues | 176,360 | 132,444 | 480,491 | 341,534 |
        | Expenses: |  |  |  |  |  |
        |  | Depreciation and amortization | 33,258 | 26,693 | 93,050 | 65,749 |
        |  | Interest expense | 17,716 | 12,622 | 44,282 | 32,329 |
        |  | Property taxes and insurance | 2,852 | 2,326 | 7,468 | 6,508 |
        |  | Senior housing operating expenses | 5,852 | — | 12,690 | — |
        |  | Transaction costs | 8,933 | 560 | 9,492 | 1,509 |
        |  | Property operating (recoveries) expenses | (108) | 279 | 184 | 1,322 |
        |  | General and administrative | 15,615 | 15,420 | 45,729 | 36,992 |
        |  | Total expenses | 84,118 | 58,352 | 217,566 | 144,861 |
        | Other (loss) income: |  |  |  |  |  |
        |  | Total other (loss) income | (7,139) | 2,915 | (5,275) | 14,459 |
        | Income before income tax expense |  | 85,103 | 77,007 | 257,650 | 211,132 |
        |  | Income tax expense | (2,929) | (2,077) | (7,735) | (3,107) |
        | Net income |  | 82,174 | 74,930 | 249,915 | 208,025 |
        |  | Net (loss) income attributable to noncontrolling interests | (1,479) | 29 | (2,944) | (1,223) |
        | Net income attributable to CareTrust REIT, Inc. |  | 83,653 | 74,901 | 252,859 | 209,248 |
        """;

    public const string BalanceSheet = """
        | CARETRUST REIT, INC. |  |  |
        | --- | --- | --- |
        | CONSOLIDATED BALANCE SHEETS |  |  |
        | (in thousands) |  |  |
        | (Unaudited) |  |  |
        |  | September 30, 2026 | December 31, 2025 |
        | Assets: |  |  |
        | Real estate investments, net | 4,717,309 | 3,709,576 |
        | Cash and cash equivalents | 479,876 | 198,042 |
        | Accounts and other receivables | 23,326 | 10,368 |
        | Prepaid expenses and other assets, net | 818,347 | 230,427 |
        | Deferred financing costs, net | 6,484 | 8,568 |
        | Total assets | 7,706,317 | 5,148,436 |
        | Liabilities and Equity: |  |  |
        | Senior unsecured notes payable, net | 398,482 | 397,816 |
        | Senior unsecured term loan, net | 497,015 | 496,404 |
        | Accounts payable, accrued liabilities and deferred rent liabilities | 165,213 | 120,442 |
        | Dividends and distributions payable | 100,606 | 74,806 |
        | Total liabilities | 2,336,316 | 1,089,468 |
        | Redeemable noncontrolling interests | 30,300 | 18,156 |
        | Common stock | 2,575 | 2,227 |
        | Additional paid-in capital | 5,850,070 | 4,518,977 |
        | Total stockholders' equity | 5,330,388 | 4,035,280 |
        | Total equity | 5,339,701 | 4,040,812 |
        | Total liabilities and equity | 7,706,317 | 5,148,436 |
        """;

    // Each income statement line as the 10-Q of the third quarter of 2025 reported it: tag,
    // the label on the statement's face, the quarter and the nine months in thousands.
    public static readonly (
        string Tag,
        string Label,
        decimal Quarter,
        decimal NineMonths
    )[] Flows =
    [
        ("OperatingLeaseLeaseIncome", "Rental income", 104_265, 261_944),
        (
            "ctre:InterestIncomeFinancingReceivables",
            "Interest income from financing receivables",
            2_908,
            8_601
        ),
        (
            "ctre:InterestAndOtherIncome",
            "Interest income from other real estate related investments and other income",
            25_271,
            70_989
        ),
        ("Revenues", "Total revenues", 132_444, 341_534),
        ("DepreciationAndAmortization", "Depreciation and amortization", 26_693, 65_749),
        ("InterestExpense", "Interest expense", 12_622, 32_329),
        ("ctre:PropertyTaxesAndInsurance", "Property taxes and insurance", 2_326, 6_508),
        ("ctre:TransactionCosts", "Transaction costs", 560, 1_509),
        ("GeneralAndAdministrativeExpense", "General and administrative", 15_420, 36_992),
        ("CostsAndExpenses", "Total expenses", 58_352, 144_861),
        ("NonoperatingIncomeExpense", "Total other (loss) income", 2_915, 14_459),
        (
            "IncomeLossFromContinuingOperationsBeforeIncomeTaxesExtraordinaryItemsNoncontrollingInterest",
            "Income before income tax expense",
            77_007,
            211_132
        ),
        ("IncomeTaxExpenseBenefit", "Income tax expense", 2_077, 3_107),
        ("ProfitLoss", "Net income", 74_930, 208_025),
        ("NetIncomeLoss", "Net income attributable to CareTrust REIT, Inc.", 74_901, 209_248),
    ];

    // The 2025 year-end balance sheet as the 10-K reported it.
    public static readonly (string Tag, string Label, decimal Value)[] Balances =
    [
        ("RealEstateInvestmentPropertyNet", "Real estate investments, net", 3_709_576),
        ("CashAndCashEquivalentsAtCarryingValue", "Cash and cash equivalents", 198_042),
        ("ctre:AccountsAndOtherReceivables", "Accounts and other receivables", 10_368),
        ("ctre:PrepaidExpensesAndOtherAssets", "Prepaid expenses and other assets, net", 230_427),
        ("ctre:DeferredFinancingCosts", "Deferred financing costs, net", 8_568),
        ("Assets", "Total assets", 5_148_436),
        ("SeniorNotes", "Senior unsecured notes payable, net", 397_816),
        ("ctre:TermLoan", "Senior unsecured term loan, net", 496_404),
        (
            "ctre:AccountsPayableAndAccruedLiabilities",
            "Accounts payable, accrued liabilities and deferred rent liabilities",
            120_442
        ),
        ("DividendsPayableCurrentAndNoncurrent", "Dividends and distributions payable", 74_806),
        ("Liabilities", "Total liabilities", 1_089_468),
        (
            "RedeemableNoncontrollingInterestEquityCarryingAmount",
            "Redeemable noncontrolling interests",
            18_156
        ),
        ("CommonStockValue", "Common stock", 2_227),
        ("AdditionalPaidInCapital", "Additional paid-in capital", 4_518_977),
        ("StockholdersEquity", "Total stockholders' equity", 4_035_280),
        (
            "StockholdersEquityIncludingPortionAttributableToNoncontrollingInterest",
            "Total equity",
            4_040_812
        ),
        ("LiabilitiesAndStockholdersEquity", "Total liabilities and equity", 5_148_436),
    ];
}
