using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Web.Services;

/// <summary>
/// Restates a 13F row's share count onto today's post-split basis, from the row's own
/// <see cref="InstitutionalHolding.ReportDate"/> (a combined-quarter view mixes report dates,
/// so a page-wide date would miss carried rows). Restatement is per exact listed series:
/// a split attributed to one share class must never multiply a sibling listing's count
/// (see <see cref="PriceSeriesSplitScope.ForListing"/>). Dollar values stay as filed —
/// they are split-invariant.
/// </summary>
public static class HoldingShareRestatement
{
    /// <param name="effectiveSplits">
    /// The stock's splits already filtered to effective-as-of-today
    /// (<c>StockSplitRepository.GetEffectiveByStock</c>) — an announced but not yet
    /// effective split must never restate anything.
    /// </param>
    public static long RestateToToday(
        InstitutionalHolding holding,
        IReadOnlyList<StockSplit> effectiveSplits,
        string primaryTicker
    )
    {
        if (effectiveSplits == null || effectiveSplits.Count == 0)
            return holding.Shares;
        var scoped = PriceSeriesSplitScope.ForListing(
            effectiveSplits,
            primaryTicker,
            holding.ListedTicker ?? primaryTicker
        );
        return SplitAdjustment.AdjustShareCount(holding.Shares, holding.ReportDate, scoped);
    }
}
