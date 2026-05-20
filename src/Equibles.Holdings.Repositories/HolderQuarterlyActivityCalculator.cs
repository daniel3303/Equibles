using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.Holdings.Repositories;

public static class HolderQuarterlyActivityCalculator
{
    // Both inputs must be materialized with the CommonStock navigation populated
    // (Include(h => h.CommonStock) at the query site) — the calculator only reads
    // loaded references.
    public static Dictionary<StockPositionChangeType, List<StockPositionChange>> Group(
        IReadOnlyList<InstitutionalHolding> currentHoldings,
        IReadOnlyList<InstitutionalHolding> previousHoldings
    )
    {
        var buckets = new Dictionary<StockPositionChangeType, List<StockPositionChange>>
        {
            [StockPositionChangeType.Initiated] = [],
            [StockPositionChangeType.Increased] = [],
            [StockPositionChangeType.Reduced] = [],
            [StockPositionChangeType.Exited] = [],
            [StockPositionChangeType.Unchanged] = [],
        };

        var currentByStock = AggregateByStock(currentHoldings);
        var previousByStock = AggregateByStock(previousHoldings);

        // % of portfolio is computed against the holder's current-quarter total value.
        // Anchoring on the current side keeps comparisons consistent for all four
        // movement buckets except Exited; Exited rows show 0% (their current value is 0).
        var totalCurrentValue = currentByStock.Values.Sum(v => v.Value);

        foreach (var (stockId, current) in currentByStock)
        {
            previousByStock.TryGetValue(stockId, out var previous);
            var changeType = ClassifyChange(current.Shares, previous?.Shares ?? 0);
            buckets[changeType].Add(BuildChange(current, previous, totalCurrentValue, changeType));
        }

        foreach (var (stockId, previous) in previousByStock)
        {
            if (currentByStock.ContainsKey(stockId))
                continue;
            buckets[StockPositionChangeType.Exited]
                .Add(BuildExitedChange(previous, totalCurrentValue));
        }

        return buckets;
    }

    private static StockPositionChangeType ClassifyChange(long currentShares, long previousShares)
    {
        if (previousShares == 0)
            return StockPositionChangeType.Initiated;
        if (currentShares == previousShares)
            return StockPositionChangeType.Unchanged;
        return currentShares > previousShares
            ? StockPositionChangeType.Increased
            : StockPositionChangeType.Reduced;
    }

    private static StockPositionChange BuildChange(
        StockAggregate current,
        StockAggregate previous,
        long totalCurrentValue,
        StockPositionChangeType changeType
    )
    {
        return new StockPositionChange
        {
            CommonStockId = current.StockId,
            Ticker = current.Ticker,
            Name = current.Name,
            CurrentShares = current.Shares,
            CurrentValue = current.Value,
            PreviousShares = previous?.Shares ?? 0,
            PreviousValue = previous?.Value ?? 0,
            ChangeType = changeType,
            PercentOfPortfolio =
                totalCurrentValue > 0 ? (double)current.Value / totalCurrentValue * 100.0 : 0,
        };
    }

    private static StockPositionChange BuildExitedChange(
        StockAggregate previous,
        long totalCurrentValue
    )
    {
        return new StockPositionChange
        {
            CommonStockId = previous.StockId,
            Ticker = previous.Ticker,
            Name = previous.Name,
            CurrentShares = 0,
            CurrentValue = 0,
            PreviousShares = previous.Shares,
            PreviousValue = previous.Value,
            ChangeType = StockPositionChangeType.Exited,
            PercentOfPortfolio = 0,
        };
    }

    private static Dictionary<Guid, StockAggregate> AggregateByStock(
        IReadOnlyList<InstitutionalHolding> holdings
    )
    {
        return holdings
            .GroupBy(h => h.CommonStockId)
            .ToDictionary(
                g => g.Key,
                g => new StockAggregate
                {
                    StockId = g.Key,
                    Ticker = g.First().CommonStock?.Ticker,
                    Name = g.First().CommonStock?.Name,
                    Shares = g.Sum(h => h.Shares),
                    Value = g.Sum(h => h.Value),
                }
            );
    }

    private class StockAggregate
    {
        public Guid StockId { get; set; }
        public string Ticker { get; set; }
        public string Name { get; set; }
        public long Shares { get; set; }
        public long Value { get; set; }
    }
}
