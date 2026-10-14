using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.Splits;

/// <summary>
/// The one rule restating a fact onto today's share basis: statement figures are on their filing
/// date's basis (ASC 260, SAB Topic 4C, IAS 33), literal counts on their as-of date. Only common-share
/// counts and per-share values move; preferred, authorized and other share quantities stay as filed.
/// </summary>
public static class FactSplitBasis
{
    // The aliases naming the issuer's common-share count; every concept they list is restated.
    private static readonly string[] CommonShareCountAliases =
    [
        "shares-outstanding",
        "weighted-average-shares-basic",
        "weighted-average-shares-diluted",
    ];

    // Common-share counts a statement restates that no alias names (charts reach raw concepts).
    private static readonly (FactTaxonomy Taxonomy, string Tag)[] UnaliasedCommonShareCounts =
    [
        (FactTaxonomy.UsGaap, "CommonStockSharesIssued"),
        (FactTaxonomy.UsGaap, "TreasuryStockShares"),
        (FactTaxonomy.UsGaap, "TreasuryStockCommonShares"),
        (FactTaxonomy.IfrsFull, "WeightedAverageShares"),
        (FactTaxonomy.IfrsFull, "AdjustedWeightedAverageShares"),
    ];

    private static readonly HashSet<(FactTaxonomy Taxonomy, string Tag)> CommonShareCountConcepts =
        CommonShareCountAliases
            .SelectMany(alias =>
                FinancialConceptAliases.TryResolve(alias, out var concepts)
                    ? concepts
                    : throw new InvalidOperationException($"Unknown share-count alias '{alias}'.")
            )
            .Select(concept => (concept.Taxonomy, concept.Tag))
            .Concat(UnaliasedCommonShareCounts)
            .Select(concept => (concept.Taxonomy, Fold(concept.Tag)))
            .ToHashSet();

    /// <summary>
    /// Classifies a fact by the concept it was reported under. A per-share unit needs no concept;
    /// a share count without one is a caller bug, since only the concept says whether a split
    /// moves it.
    /// </summary>
    public static FactSplitKind Classify(FinancialConceptAliases.ConceptRef concept, string unit)
    {
        if (concept != null)
            return Classify(concept.Taxonomy, concept.Tag, unit);
        if (IsShareUnit(unit))
            throw new ArgumentException(
                "A share-count fact needs its concept to decide whether a split moves it.",
                nameof(concept)
            );
        return IsPerShareUnit(unit) ? FactSplitKind.PerShare : FactSplitKind.None;
    }

    /// <summary>
    /// Classifies a figure read from a report without XBRL concepts, known only by its alias. Such
    /// reports are IFRS: IAS 33 restates per-share values and weighted-average counts, but the
    /// period-end share count stays the legal count on its date (a later split is non-adjusting).
    /// </summary>
    public static FactSplitKind ClassifyStatementFigure(string alias, string unit, bool isPoint)
    {
        if (IsPerShareUnit(unit))
            return FactSplitKind.PerShare;
        if (!IsShareUnit(unit) || !CommonShareCountAliases.Contains(alias))
            return FactSplitKind.None;
        return isPoint ? FactSplitKind.ObservedShareCount : FactSplitKind.StatedShareCount;
    }

    public static FactSplitKind Classify(FactTaxonomy taxonomy, string tag, string unit)
    {
        if (IsPerShareUnit(unit))
            return FactSplitKind.PerShare;
        if (!IsShareUnit(unit) || !CommonShareCountConcepts.Contains((taxonomy, Fold(tag))))
            return FactSplitKind.None;
        return taxonomy == FactTaxonomy.Dei
            ? FactSplitKind.ObservedShareCount
            : FactSplitKind.StatedShareCount;
    }

    /// <summary>The date whose share basis the value is stated on.</summary>
    public static DateOnly BasisDate(FactSplitKind kind, DateOnly periodEnd, DateOnly filedDate) =>
        kind == FactSplitKind.ObservedShareCount ? periodEnd : filedDate;

    /// <summary>
    /// Restates a fact by the issuer's share-count events seen from one exact listing
    /// (<see cref="ShareCountSplitScope"/>). Splits that cannot be attributed after the basis date
    /// keep the filed value, marked unresolved.
    /// </summary>
    public static FactSplitRestatement Restate(
        FinancialFact fact,
        FactSplitKind kind,
        IReadOnlyCollection<StockSplit> splits,
        Guid listingId
    ) => Restate(fact.Value, kind, fact.PeriodEnd, fact.FiledDate, splits, listingId);

    public static FactSplitRestatement Restate(
        decimal value,
        FactSplitKind kind,
        DateOnly periodEnd,
        DateOnly filedDate,
        IReadOnlyCollection<StockSplit> splits,
        Guid listingId
    )
    {
        if (kind == FactSplitKind.None)
            return new FactSplitRestatement(value, kind, Adjusted: false, Unresolved: false);
        var basisDate = BasisDate(kind, periodEnd, filedDate);
        if (ShareCountSplitScope.HasUnresolvedBasis(splits, listingId, basisDate))
            return new FactSplitRestatement(value, kind, Adjusted: false, Unresolved: true);
        return Apply(
            value,
            kind,
            SplitAdjustment.ShareCountFactor(
                basisDate,
                ShareCountSplitScope.ForListing(splits, listingId)
            )
        );
    }

    /// <summary>
    /// Restates a value by splits the caller already scoped, for figures compared against one
    /// price series rather than shown for a listing.
    /// </summary>
    public static decimal RestateScoped(
        decimal value,
        FactSplitKind kind,
        DateOnly periodEnd,
        DateOnly filedDate,
        IEnumerable<StockSplit> scopedSplits
    ) =>
        kind == FactSplitKind.None
            ? value
            : Apply(
                value,
                kind,
                SplitAdjustment.ShareCountFactor(
                    BasisDate(kind, periodEnd, filedDate),
                    scopedSplits
                )
            ).Value;

    private static FactSplitRestatement Apply(decimal value, FactSplitKind kind, decimal factor)
    {
        if (factor is 1m or <= 0m)
            return new FactSplitRestatement(value, kind, Adjusted: false, Unresolved: false);
        var restated = kind == FactSplitKind.PerShare ? value / factor : value * factor;
        return new FactSplitRestatement(restated, kind, Adjusted: true, Unresolved: false);
    }

    // Filers spell one tag with stray underscores or case; the fold makes them one concept.
    private static string Fold(string tag) => tag?.Replace("_", "").ToLowerInvariant();

    // Only a share-denominated ratio is per-share: USD/bbl, USD/EUR or shares/USD stay as filed.
    private static bool IsPerShareUnit(string unit)
    {
        if (unit == null)
            return false;
        var separator = unit.IndexOf('/', StringComparison.Ordinal);
        return separator > 0
            && separator == unit.LastIndexOf('/')
            && IsShareUnit(unit[(separator + 1)..]);
    }

    private static bool IsShareUnit(string unit)
    {
        var trimmed = unit?.Trim();
        return string.Equals(trimmed, "shares", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "share", StringComparison.OrdinalIgnoreCase);
    }
}
