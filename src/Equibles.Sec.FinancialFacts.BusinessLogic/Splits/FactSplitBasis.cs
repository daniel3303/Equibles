using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.Splits;

/// <summary>
/// The one rule for restating a financial fact onto today's share basis, shared by every surface
/// that serves facts. A filing issued after a split restates every per-share amount and share
/// count its statements present (ASC 260, IAS 33, SAB Topic 4C), so a statement figure is on the
/// basis of its filing date. The cover-page count is a literal count on its as-of date. Only the
/// issuer's common-share counts listed here move with a split; preferred, authorized and other
/// share quantities stay as filed.
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
    /// Classifies a statement figure known only by its alias, as read from a report without XBRL
    /// concepts. Such figures carry no cover page, so a count is a stated count.
    /// </summary>
    public static FactSplitKind ClassifyStatementFigure(string alias, string unit)
    {
        if (IsPerShareUnit(unit))
            return FactSplitKind.PerShare;
        return IsShareUnit(unit) && CommonShareCountAliases.Contains(alias)
            ? FactSplitKind.StatedShareCount
            : FactSplitKind.None;
    }

    public static FactSplitKind Classify(FactTaxonomy taxonomy, string tag, string unit)
    {
        if (IsPerShareUnit(unit))
            return FactSplitKind.PerShare;
        if (!IsShareUnit(unit) || !CommonShareCountConcepts.Contains((taxonomy, tag)))
            return FactSplitKind.None;
        return taxonomy == FactTaxonomy.Dei
            ? FactSplitKind.ObservedShareCount
            : FactSplitKind.StatedShareCount;
    }

    /// <summary>The date whose share basis the value is stated on.</summary>
    public static DateOnly BasisDate(FactSplitKind kind, DateOnly periodEnd, DateOnly filedDate) =>
        kind == FactSplitKind.ObservedShareCount ? periodEnd : filedDate;

    /// <summary>
    /// Restates a fact by the splits of one exact listing. Splits the listing cannot claim
    /// unambiguously after the basis date keep the filed value, marked unresolved.
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
        if (PriceSeriesSplitScope.HasUnresolvedBasis(splits, listingId, basisDate))
            return new FactSplitRestatement(value, kind, Adjusted: false, Unresolved: true);
        return Apply(
            value,
            kind,
            SplitAdjustment.ShareCountFactor(
                basisDate,
                PriceSeriesSplitScope.ForListing(splits, listingId)
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
