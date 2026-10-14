namespace Equibles.Sec.FinancialFacts.Mcp.Helpers;

// Footnotes for tables restated through FactSplitBasis.
internal static class FinancialFactSplitAdjustment
{
    internal const string Note =
        "Per-share values and common-share counts are split-adjusted to today's share basis: "
        + "statement figures by splits effective strictly after each fact's Filed date, "
        + "cover-page share counts by splits after their as-of date.";

    internal const string UnresolvedNote =
        "Values marked 'as filed' retain the source value because split attribution is unresolved; "
        + "they have not been normalized to today's share basis.";
}
