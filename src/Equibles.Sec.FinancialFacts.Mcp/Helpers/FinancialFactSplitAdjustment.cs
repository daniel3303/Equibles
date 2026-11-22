namespace Equibles.Sec.FinancialFacts.Mcp.Helpers;

// Footnotes for tables restated through FactSplitBasis.
internal static class FinancialFactSplitAdjustment
{
    internal const string Note =
        "Per-share values and common-share counts are split-adjusted to today's share basis: "
        + "statement figures by splits effective strictly after each fact's Filed date, "
        + "cover-page share counts by splits after their as-of date.";

    // A card's short lines for the two notes above.
    internal const string CardNote = "Per-share figures and share counts are restated for splits.";
    internal const string UnresolvedCardNote =
        "Rows marked 'as filed' are not restated for splits.";

    internal const string UnresolvedNote =
        "Values marked 'as filed' retain the source value because split attribution is unresolved; "
        + "they have not been normalized to today's share basis.";
}
