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

        var currentBySecurity = AggregateBySecurity(currentHoldings);
        var previousBySecurity = AggregateBySecurity(previousHoldings);

        // % of portfolio is computed against the holder's current-quarter total value.
        // Anchoring on the current side keeps comparisons consistent for all four
        // movement buckets except Exited; Exited rows show 0% (their current value is 0).
        var totalCurrentValue = currentBySecurity.Values.Sum(v => v.Value);

        foreach (var (security, current) in currentBySecurity)
        {
            previousBySecurity.TryGetValue(security, out var previous);
            var changeType = ClassifyChange(current.Shares, previous?.Shares ?? 0);
            buckets[changeType].Add(BuildChange(current, previous, totalCurrentValue, changeType));
        }

        foreach (var (security, previous) in previousBySecurity)
        {
            if (currentBySecurity.ContainsKey(security))
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
        if (currentShares == 0)
            return StockPositionChangeType.Exited;
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
            PrimaryTicker = current.PrimaryTicker,
            ListedTicker = current.ListedTicker,
            Ticker = current.Ticker,
            Name = current.Name,
            CurrentShares = current.Shares,
            CurrentValue = current.Value,
            PreviousShares = previous?.Shares ?? 0,
            PreviousValue = previous?.Value ?? 0,
            ChangeType = changeType,
            PercentOfPortfolio = Percentage.Of(current.Value, totalCurrentValue),
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
            PrimaryTicker = previous.PrimaryTicker,
            ListedTicker = previous.ListedTicker,
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

    private static Dictionary<SecurityKey, StockAggregate> AggregateBySecurity(
        IReadOnlyList<InstitutionalHolding> holdings
    )
    {
        return holdings
            .GroupBy(h => new SecurityKey(h.CommonStockId, h.ListedTicker ?? h.CommonStock?.Ticker))
            .ToDictionary(
                g => g.Key,
                g => new StockAggregate
                {
                    StockId = g.Key.CommonStockId,
                    PrimaryTicker = g.First().CommonStock?.Ticker,
                    ListedTicker = g.Key.ListedTicker,
                    Ticker = g.Key.ListedTicker,
                    Name = g.First().CommonStock?.Name,
                    Shares = g.Sum(h => h.Shares),
                    Value = g.Sum(h => h.Value),
                }
            );
    }

    private class StockAggregate
    {
        public Guid StockId { get; set; }
        public string PrimaryTicker { get; set; }
        public string ListedTicker { get; set; }
        public string Ticker { get; set; }
        public string Name { get; set; }
        public long Shares { get; set; }
        public long Value { get; set; }
    }

    private readonly record struct SecurityKey(Guid CommonStockId, string ListedTicker);
}
