using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.Holdings.Repositories.Extensions;

// Maps the materialised per-(stock, quarter) snapshot rows onto the same shapes the
// live aggregations produce, so the shared bucket definitions (TopBuyers/TopSellers/
// NewPositions/SoldOutPositions, most-held sorts) apply unchanged whether a surface
// read the snapshot or ran the query live. One mapper for both lanes — the plain
// closed-quarter snapshot and its open-window combined twin carry identical columns.
public static class StockActivitySnapshotMapping
{
    public static MarketWideStockActivity ToActivity(this StockQuarterlyActivity s) =>
        new()
        {
            CommonStockId = s.CommonStockId,
            CurrentShares = s.CurrentShares,
            PreviousShares = s.PreviousShares,
            CurrentValue = s.CurrentValue,
            PreviousValue = s.PreviousValue,
            CurrentFilerCount = s.CurrentFilerCount,
            PreviousFilerCount = s.PreviousFilerCount,
        };

    public static MarketWideStockChurn ToChurn(this StockQuarterlyActivity s) =>
        new()
        {
            CommonStockId = s.CommonStockId,
            NewFilerCount = s.NewFilerCount,
            SoldOutFilerCount = s.SoldOutFilerCount,
        };

    public static MarketWideStockActivity ToActivity(this StockQuarterlyActivityCombined s) =>
        new()
        {
            CommonStockId = s.CommonStockId,
            CurrentShares = s.CurrentShares,
            PreviousShares = s.PreviousShares,
            CurrentValue = s.CurrentValue,
            PreviousValue = s.PreviousValue,
            CurrentFilerCount = s.CurrentFilerCount,
            PreviousFilerCount = s.PreviousFilerCount,
        };

    public static MarketWideStockChurn ToChurn(this StockQuarterlyActivityCombined s) =>
        new()
        {
            CommonStockId = s.CommonStockId,
            NewFilerCount = s.NewFilerCount,
            SoldOutFilerCount = s.SoldOutFilerCount,
        };
}
