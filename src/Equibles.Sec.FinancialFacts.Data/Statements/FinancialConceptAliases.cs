using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// Maps a friendly concept name (e.g. <c>"revenue"</c>, <c>"net-income"</c>) to
/// the ordered set of XBRL (taxonomy, tag) pairs that express it. Shared by the
/// FinancialFacts MCP tools so callers need not know SEC tag names. A single
/// alias can map to several tags because companies switch concepts over time
/// (e.g. <c>Revenues</c> → <c>RevenueFromContractWithCustomerExcludingAssessedTax</c>
/// after ASC 606); callers query the union and pick per period.
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

    private static readonly Dictionary<string, IReadOnlyList<ConceptRef>> Map = new()
    {
        ["revenue"] =
        [
            new(FactTaxonomy.UsGaap, "Revenues"),
            new(FactTaxonomy.UsGaap, "RevenueFromContractWithCustomerExcludingAssessedTax"),
        ],
        ["cost-of-revenue"] = [new(FactTaxonomy.UsGaap, "CostOfRevenue")],
        ["gross-profit"] = [new(FactTaxonomy.UsGaap, "GrossProfit")],
        ["operating-expenses"] = [new(FactTaxonomy.UsGaap, "OperatingExpenses")],
        ["research-and-development"] = [new(FactTaxonomy.UsGaap, "ResearchAndDevelopmentExpense")],
        ["operating-income"] = [new(FactTaxonomy.UsGaap, "OperatingIncomeLoss")],
        ["net-income"] = [new(FactTaxonomy.UsGaap, "NetIncomeLoss")],
        ["eps-basic"] = [new(FactTaxonomy.UsGaap, "EarningsPerShareBasic")],
        ["eps-diluted"] = [new(FactTaxonomy.UsGaap, "EarningsPerShareDiluted")],
        ["total-assets"] = [new(FactTaxonomy.UsGaap, "Assets")],
        ["current-assets"] = [new(FactTaxonomy.UsGaap, "AssetsCurrent")],
        ["total-liabilities"] = [new(FactTaxonomy.UsGaap, "Liabilities")],
        ["current-liabilities"] = [new(FactTaxonomy.UsGaap, "LiabilitiesCurrent")],
        ["stockholders-equity"] = [new(FactTaxonomy.UsGaap, "StockholdersEquity")],
        ["retained-earnings"] = [new(FactTaxonomy.UsGaap, "RetainedEarningsAccumulatedDeficit")],
        ["cash"] = [new(FactTaxonomy.UsGaap, "CashAndCashEquivalentsAtCarryingValue")],
        ["operating-cash-flow"] =
        [
            new(FactTaxonomy.UsGaap, "NetCashProvidedByUsedInOperatingActivities"),
        ],
        ["investing-cash-flow"] =
        [
            new(FactTaxonomy.UsGaap, "NetCashProvidedByUsedInInvestingActivities"),
        ],
        ["financing-cash-flow"] =
        [
            new(FactTaxonomy.UsGaap, "NetCashProvidedByUsedInFinancingActivities"),
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
        ["operating-profit"] = "operating-income",
        ["net-profit"] = "net-income",
        ["earnings"] = "net-income",
        ["diluted-eps"] = "eps-diluted",
        ["basic-eps"] = "eps-basic",
        ["assets"] = "total-assets",
        ["liabilities"] = "total-liabilities",
        ["equity"] = "stockholders-equity",
        ["ocf"] = "operating-cash-flow",
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
