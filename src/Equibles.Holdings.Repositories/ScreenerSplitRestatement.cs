using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.Holdings.Repositories;

/// <summary>
/// One stock's current-quarter share sum for one exact listed series
/// (<c>ListedTicker == null</c> is the primary listing), feeding the screener's
/// per-listing split restatement.
/// </summary>
public sealed class ScreenerListingShares
{
    public Guid CommonStockId { get; set; }
    public string ListedTicker { get; set; }
    public long Shares { get; set; }
}

/// <summary>
/// In-memory half of the split-aware screener: restates a split-affected row's
/// share count and % of float onto today's basis. Pure so it stays unit-testable
/// without a database.
/// </summary>
public static class ScreenerSplitRestatement
{
    /// <summary>
    /// Restates <see cref="ScreenerRow.CurrentShares"/> from the exact per-listing sums
    /// (a split attributed to one share class must never multiply a sibling listing's
    /// count) and recomputes <see cref="ScreenerRow.PercentOfFloat"/> against today's
    /// <see cref="ScreenerRow.SharesOutStanding"/>. Dollar values stay as filed.
    /// </summary>
    public static void RestateRow(
        ScreenerRow row,
        IReadOnlyList<ScreenerListingShares> currentListingShares,
        IReadOnlyList<StockSplit> splitsSinceCurrent,
        DateOnly current
    )
    {
        long restated = 0;
        foreach (var slice in currentListingShares)
        {
            var scoped = PriceSeriesSplitScope.ForListing(
                splitsSinceCurrent,
                row.Ticker,
                slice.ListedTicker ?? row.Ticker
            );
            restated += SplitAdjustment.AdjustShareCount(slice.Shares, current, scoped);
        }
        row.CurrentShares = restated;
        row.PercentOfFloat =
            row.SharesOutStanding > 0 ? (double)restated / row.SharesOutStanding * 100.0 : null;
    }

    /// <summary>
    /// The % of float criteria as the SQL predicates apply them: an absent %
    /// (unknown SharesOutStanding) fails any active bound rather than passing.
    /// </summary>
    public static bool PassesPctFloat(ScreenerRow row, ScreenerCriteria criteria) =>
        (
            !criteria.MinPctFloat.HasValue
            || (
                row.PercentOfFloat.HasValue
                && row.PercentOfFloat.Value >= criteria.MinPctFloat.Value
            )
        )
        && (
            !criteria.MaxPctFloat.HasValue
            || (
                row.PercentOfFloat.HasValue
                && row.PercentOfFloat.Value <= criteria.MaxPctFloat.Value
            )
        );
}
