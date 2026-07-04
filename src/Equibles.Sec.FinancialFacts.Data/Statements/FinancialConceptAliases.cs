using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// Maps a friendly concept name (e.g. <c>"revenue"</c>, <c>"net-income"</c>) to
/// the ordered set of XBRL (taxonomy, tag) pairs that express it. Shared by the
/// FinancialFacts MCP tools so callers need not know SEC tag names, and by
/// <see cref="FinancialStatementConcepts"/> so every statement line resolves the
/// same tag variants everywhere. A single alias can map to several tags because
/// companies switch concepts over time (e.g. <c>Revenues</c> →
/// <c>RevenueFromContractWithCustomerExcludingAssessedTax</c> after ASC 606) or
/// report a narrower variant (ADBE files R&amp;D under the software-specific
/// tag); callers query the union and pick per period in declaration order —
/// earlier tags are the broader/preferred measure, later ones only fill
/// periods the preferred tags lack.
/// </summary>
public static class FinancialConceptAliases
{
    public sealed class ConceptRef
    {
        public ConceptRef(FactTaxonomy taxonomy, string tag)
        {
            Taxonomy = taxonomy;
            Tag = tag;
        }

        public FactTaxonomy Taxonomy { get; }

        public string Tag { get; }
    }

    private static ConceptRef G(string tag) => new(FactTaxonomy.UsGaap, tag);

    private static readonly Dictionary<string, IReadOnlyList<ConceptRef>> Map = new()
    {
        // ── Income statement ────────────────────────────────────────────────
        ["revenue"] =
        [
            G("Revenues"),
            G("RevenueFromContractWithCustomerExcludingAssessedTax"),
            // Some filers (REITs like ARE) tag their total-revenue line with the
            // assessed-tax-inclusive ASC 606 variant and never file the excluding
            // form; it only fills periods the preferred tags lack.
            G("RevenueFromContractWithCustomerIncludingAssessedTax"),
            // The dominant pre-ASC 606 total-revenue tag (AAPL, MSFT, … filed it
            // through ~FY2017; deprecated 2018). Last so it only fills periods
            // the modern tags lack — without it those companies' revenue series
            // start mid-history.
            G("SalesRevenueNet"),
            // Pre-2019 real-estate total revenue (deprecated with ASC 842):
            // REITs filed it instead of SalesRevenueNet, so without it their
            // revenue series starts at the ASC 606 transition.
            G("RealEstateRevenueNet"),
        ],
        // Financial-sector top lines: banks, broker-dealers and insurers never
        // file the operating-company revenue tags above.
        ["interest-and-dividend-income"] = [G("InterestAndDividendIncomeOperating")],
        ["net-interest-income"] = [G("InterestIncomeExpenseNet")],
        ["noninterest-income"] = [G("NoninterestIncome")],
        ["total-net-revenue"] = [G("RevenuesNetOfInterestExpense")],
        ["premiums-earned"] = [G("PremiumsEarnedNet")],
        // REIT top-line detail: lessors report rental revenue under the lease
        // tags, not the contract-with-customer tags. LeaseIncome (operating +
        // sales-type/direct-financing income) is the broadest; the operating
        // component fills filers that never report the total; the pre-ASC 842
        // tag covers history before 2019.
        ["rental-income"] =
        [
            G("LeaseIncome"),
            G("OperatingLeaseLeaseIncome"),
            G("OperatingLeasesIncomeStatementLeaseRevenue"),
        ],
        ["cost-of-revenue"] =
        [
            G("CostOfRevenue"),
            G("CostOfGoodsAndServicesSold"),
            G("CostOfGoodsSold"),
        ],
        ["gross-profit"] = [G("GrossProfit")],
        ["research-and-development"] =
        [
            G("ResearchAndDevelopmentExpense"),
            // Software companies (ADBE, …) report R&D under the
            // software-development-cost variant instead of the generic tag.
            G("ResearchAndDevelopmentExpenseSoftwareExcludingAcquiredInProcessCost"),
            G("ResearchAndDevelopmentExpenseExcludingAcquiredInProcessCost"),
        ],
        ["selling-general-and-administrative"] = [G("SellingGeneralAndAdministrativeExpense")],
        ["selling-and-marketing"] = [G("SellingAndMarketingExpense")],
        ["general-and-administrative"] = [G("GeneralAndAdministrativeExpense")],
        ["amortization-of-intangibles"] = [G("AmortizationOfIntangibleAssets")],
        ["restructuring"] = [G("RestructuringCharges")],
        ["operating-expenses"] = [G("OperatingExpenses")],
        // Companies that present no gross-profit subtotal report this combined
        // figure instead; it INCLUDES cost of revenue, so it is a separate
        // line, never a fallback for operating-expenses.
        ["total-costs-and-expenses"] = [G("CostsAndExpenses")],
        ["operating-income"] = [G("OperatingIncomeLoss")],
        ["interest-expense"] = [G("InterestExpense"), G("InterestExpenseNonoperating")],
        ["interest-income"] = [G("InvestmentIncomeInterest")],
        ["other-nonoperating-income"] =
        [
            G("NonoperatingIncomeExpense"),
            G("OtherNonoperatingIncomeExpense"),
        ],
        ["pretax-income"] =
        [
            G(
                "IncomeLossFromContinuingOperationsBeforeIncomeTaxesExtraordinaryItemsNoncontrollingInterest"
            ),
            G(
                "IncomeLossFromContinuingOperationsBeforeIncomeTaxesMinorityInterestAndIncomeLossFromEquityMethodInvestments"
            ),
        ],
        ["income-tax"] = [G("IncomeTaxExpenseBenefit")],
        // ProfitLoss (net income including noncontrolling interests) last: it
        // only fills filers that never report the parent-attributable figure.
        ["net-income"] = [G("NetIncomeLoss"), G("ProfitLoss")],
        ["comprehensive-income"] = [G("ComprehensiveIncomeNetOfTax")],
        ["eps-basic"] = [G("EarningsPerShareBasic")],
        ["eps-diluted"] = [G("EarningsPerShareDiluted")],
        ["weighted-average-shares-basic"] =
        [
            G("WeightedAverageNumberOfSharesOutstandingBasic"),
            // Filers whose basic and diluted share counts are equal (no dilutive
            // securities) report only the combined tag; it fills the basic series
            // where the split-out basic tag is absent.
            G("WeightedAverageNumberOfShareOutstandingBasicAndDiluted"),
        ],
        ["weighted-average-shares-diluted"] =
        [
            G("WeightedAverageNumberOfDilutedSharesOutstanding"),
            // The same combined tag fills the diluted series for those filers, so
            // the basic-and-diluted vintage never lists as its own picker entry.
            G("WeightedAverageNumberOfShareOutstandingBasicAndDiluted"),
        ],

        // ── Balance sheet ───────────────────────────────────────────────────
        ["cash"] =
        [
            G("CashAndCashEquivalentsAtCarryingValue"),
            // Post-ASU 2016-18 filers may only report the restricted-cash-
            // inclusive total; it fills periods the pure tag lacks.
            G("CashCashEquivalentsRestrictedCashAndRestrictedCashEquivalents"),
        ],
        ["short-term-investments"] = [G("ShortTermInvestments"), G("MarketableSecuritiesCurrent")],
        ["accounts-receivable"] = [G("AccountsReceivableNetCurrent"), G("ReceivablesNetCurrent")],
        ["inventory"] = [G("InventoryNet")],
        ["current-assets"] = [G("AssetsCurrent")],
        ["property-plant-and-equipment"] = [G("PropertyPlantAndEquipmentNet")],
        ["operating-lease-assets"] = [G("OperatingLeaseRightOfUseAsset")],
        ["goodwill"] = [G("Goodwill")],
        ["intangible-assets"] =
        [
            G("FiniteLivedIntangibleAssetsNet"),
            G("IntangibleAssetsNetExcludingGoodwill"),
        ],
        ["long-term-investments"] = [G("LongTermInvestments"), G("MarketableSecuritiesNoncurrent")],
        ["total-assets"] = [G("Assets")],
        ["accounts-payable"] = [G("AccountsPayableCurrent"), G("AccountsPayableTradeCurrent")],
        ["accrued-liabilities"] = [G("AccruedLiabilitiesCurrent")],
        ["deferred-revenue"] =
        [
            G("ContractWithCustomerLiabilityCurrent"),
            G("DeferredRevenueCurrent"),
        ],
        ["short-term-debt"] =
        [
            G("DebtCurrent"),
            G("LongTermDebtCurrent"),
            G("ShortTermBorrowings"),
        ],
        ["current-liabilities"] = [G("LiabilitiesCurrent")],
        // LongTermDebt (the total including the current portion) last: it only
        // fills filers that never split out the noncurrent portion.
        ["long-term-debt"] = [G("LongTermDebtNoncurrent"), G("LongTermDebt")],
        ["operating-lease-liabilities"] =
        [
            G("OperatingLeaseLiabilityNoncurrent"),
            G("OperatingLeaseLiability"),
        ],
        ["deferred-revenue-noncurrent"] =
        [
            G("ContractWithCustomerLiabilityNoncurrent"),
            G("DeferredRevenueNoncurrent"),
        ],
        ["total-liabilities"] = [G("Liabilities")],
        ["common-stock-value"] =
        [
            G("CommonStockValue"),
            G("CommonStocksIncludingAdditionalPaidInCapital"),
        ],
        ["additional-paid-in-capital"] = [G("AdditionalPaidInCapital")],
        ["retained-earnings"] = [G("RetainedEarningsAccumulatedDeficit")],
        ["accumulated-oci"] = [G("AccumulatedOtherComprehensiveIncomeLossNetOfTax")],
        ["treasury-stock"] = [G("TreasuryStockValue"), G("TreasuryStockCommonValue")],
        ["stockholders-equity"] =
        [
            G("StockholdersEquity"),
            G("StockholdersEquityIncludingPortionAttributableToNoncontrollingInterest"),
        ],
        ["total-liabilities-and-equity"] = [G("LiabilitiesAndStockholdersEquity")],
        // The issuer's common shares outstanding at a point in time. The dei cover-page
        // count (as of the filing date) leads; the us-gaap balance-sheet count fills the
        // periods the cover-page tag lacks. Both name the same measure — one line, not two
        // near-duplicate picker entries. Single-class filers report the cover-page tag
        // consolidated (no dimension); multi-class filers report it only per share class,
        // so a consolidated fact is absent and the entity total is the sum across classes.
        ["shares-outstanding"] =
        [
            new(FactTaxonomy.Dei, "EntityCommonStockSharesOutstanding"),
            G("CommonStockSharesOutstanding"),
        ],

        // ── Cash flow ───────────────────────────────────────────────────────
        ["depreciation-and-amortization"] =
        [
            G("DepreciationDepletionAndAmortization"),
            G("DepreciationAmortizationAndAccretionNet"),
            // Capital-heavy filers (REITs) tag the cash-flow add-back with the
            // plain income-statement element instead of the DD&A variants.
            G("DepreciationAndAmortization"),
        ],
        ["share-based-compensation"] = [G("ShareBasedCompensation")],
        ["deferred-income-taxes"] =
        [
            G("DeferredIncomeTaxExpenseBenefit"),
            G("DeferredIncomeTaxesAndTaxCredits"),
        ],
        ["operating-cash-flow"] =
        [
            G("NetCashProvidedByUsedInOperatingActivities"),
            G("NetCashProvidedByUsedInOperatingActivitiesContinuingOperations"),
        ],
        ["capital-expenditures"] =
        [
            G("PaymentsToAcquirePropertyPlantAndEquipment"),
            G("PaymentsToAcquireProductiveAssets"),
        ],
        ["acquisitions"] = [G("PaymentsToAcquireBusinessesNetOfCashAcquired")],
        ["investing-cash-flow"] =
        [
            G("NetCashProvidedByUsedInInvestingActivities"),
            G("NetCashProvidedByUsedInInvestingActivitiesContinuingOperations"),
        ],
        ["share-repurchases"] = [G("PaymentsForRepurchaseOfCommonStock")],
        ["dividends-paid"] = [G("PaymentsOfDividends"), G("PaymentsOfDividendsCommonStock")],
        ["debt-issued"] = [G("ProceedsFromIssuanceOfLongTermDebt")],
        ["debt-repaid"] = [G("RepaymentsOfLongTermDebt"), G("RepaymentsOfDebt")],
        ["stock-issued"] = [G("ProceedsFromIssuanceOfCommonStock")],
        ["financing-cash-flow"] =
        [
            G("NetCashProvidedByUsedInFinancingActivities"),
            G("NetCashProvidedByUsedInFinancingActivitiesContinuingOperations"),
        ],
        ["fx-effect-on-cash"] =
        [
            G(
                "EffectOfExchangeRateOnCashCashEquivalentsRestrictedCashAndRestrictedCashEquivalents"
            ),
            G("EffectOfExchangeRateOnCashAndCashEquivalents"),
        ],
        ["net-change-in-cash"] =
        [
            // The modern (post-ASU 2016-18) tag first; the pre-2018 tag fills
            // older periods, the exchange-rate-excluding variant fills filers
            // that only report that shape.
            G(
                "CashCashEquivalentsRestrictedCashAndRestrictedCashEquivalentsPeriodIncreaseDecreaseIncludingExchangeRateEffect"
            ),
            G("CashAndCashEquivalentsPeriodIncreaseDecrease"),
            G(
                "CashCashEquivalentsRestrictedCashAndRestrictedCashEquivalentsPeriodIncreaseDecreaseExcludingExchangeRateEffect"
            ),
        ],
    };

    // Spelt-out synonyms normalise onto a canonical key so callers can use
    // natural phrasing ("net income", "r&d", "diluted eps").
    private static readonly Dictionary<string, string> Synonyms = new()
    {
        ["sales"] = "revenue",
        ["net-revenue"] = "revenue",
        ["total-revenue"] = "revenue",
        ["cogs"] = "cost-of-revenue",
        ["rnd"] = "research-and-development",
        ["r-and-d"] = "research-and-development",
        ["r&d"] = "research-and-development",
        ["sga"] = "selling-general-and-administrative",
        ["sg&a"] = "selling-general-and-administrative",
        ["operating-profit"] = "operating-income",
        ["net-profit"] = "net-income",
        ["earnings"] = "net-income",
        ["diluted-eps"] = "eps-diluted",
        ["basic-eps"] = "eps-basic",
        ["assets"] = "total-assets",
        ["liabilities"] = "total-liabilities",
        ["equity"] = "stockholders-equity",
        ["ocf"] = "operating-cash-flow",
        ["capex"] = "capital-expenditures",
        ["buybacks"] = "share-repurchases",
        ["stock-buybacks"] = "share-repurchases",
        ["dividends"] = "dividends-paid",
        ["d-and-a"] = "depreciation-and-amortization",
        ["d&a"] = "depreciation-and-amortization",
        ["sbc"] = "share-based-compensation",
        ["stock-based-compensation"] = "share-based-compensation",
        ["ppe"] = "property-plant-and-equipment",
        ["pp&e"] = "property-plant-and-equipment",
        ["aoci"] = "accumulated-oci",
        ["apic"] = "additional-paid-in-capital",
    };

    public static IReadOnlyCollection<string> SupportedAliases => Map.Keys;

    public static bool TryResolve(string alias, out IReadOnlyList<ConceptRef> concepts)
    {
        var key = Normalize(alias);
        if (Synonyms.TryGetValue(key, out var canonical))
            key = canonical;
        if (Map.TryGetValue(key, out var refs))
        {
            concepts = refs;
            return true;
        }

        concepts = [];
        return false;
    }

    private static string Normalize(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return string.Empty;
        return alias
            .Trim()
            .ToLowerInvariant()
            .Replace(" & ", "&")
            .Replace(' ', '-')
            .Replace('_', '-')
            .Replace("/", "-");
    }
}
