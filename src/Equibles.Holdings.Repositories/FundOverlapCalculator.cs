using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.Holdings.Repositories;

public static class FundOverlapCalculator
{
    // Each tuple is (holder, holder's holdings for the shared report date). Holdings
    // must be materialized with CommonStock populated (Include(h => h.CommonStock) at
    // the query site).
    public static FundOverlapResult Calculate(
        IReadOnlyList<(
            InstitutionalHolder Holder,
            IReadOnlyList<InstitutionalHolding> Holdings
        )> funds,
        DateOnly reportDate
    )
    {
        var result = new FundOverlapResult { ReportDate = reportDate };
        if (funds.Count == 0)
            return result;

        // Aggregate each fund's positions per stock. The aggregation collapses multiple
        // discretion rows for the same stock so the per-fund slice is one entry per
        // CommonStockId.
        var fundAggregates = funds
            .Select(f =>
            {
                var perStock = f
                    .Holdings.GroupBy(h => h.CommonStockId)
                    .ToDictionary(
                        g => g.Key,
                        g => new FundStockAggregate
                        {
                            CommonStockId = g.Key,
                            Ticker = g.First().CommonStock?.Ticker,
                            Name = g.First().CommonStock?.Name,
                            Shares = g.Sum(h => h.Shares),
                            Value = g.Sum(h => h.Value),
                        }
                    );
                return new FundAggregate { Holder = f.Holder, PerStock = perStock };
            })
            .ToList();

        foreach (var fund in fundAggregates)
        {
            result.Funds.Add(
                new FundOverlapFund
                {
                    HolderId = fund.Holder.Id,
                    HolderCik = fund.Holder.Cik,
                    HolderName = fund.Holder.Name,
                    PositionCount = fund.PerStock.Count,
                    TotalValue = fund.PerStock.Values.Sum(s => s.Value),
                }
            );
        }

        // Union of stock ids; build one row per stock, with one slice per fund.
        var allStockIds = fundAggregates.SelectMany(f => f.PerStock.Keys).Distinct().ToList();

        long dollarWeightedNumerator = 0;
        long dollarWeightedDenominator = 0;
        var intersectionCount = 0;

        foreach (var stockId in allStockIds)
        {
            string ticker = null;
            string name = null;
            var slices = new List<FundOverlapRowSlice>();
            long combinedValue = 0;
            long minValue = long.MaxValue;
            long maxValue = 0;
            var allHaveIt = true;
            foreach (var fund in fundAggregates)
            {
                fund.PerStock.TryGetValue(stockId, out var perStock);
                ticker ??= perStock?.Ticker;
                name ??= perStock?.Name;
                var fundTotal = fund.PerStock.Values.Sum(s => s.Value);
                var slice = new FundOverlapRowSlice
                {
                    HolderId = fund.Holder.Id,
                    Shares = perStock?.Shares ?? 0,
                    Value = perStock?.Value ?? 0,
                    PercentOfPortfolio =
                        fundTotal > 0 && perStock != null
                            ? (double)perStock.Value / fundTotal * 100.0
                            : 0,
                };
                slices.Add(slice);
                combinedValue += slice.Value;
                if (perStock == null)
                    allHaveIt = false;
                else
                {
                    if (perStock.Value < minValue)
                        minValue = perStock.Value;
                    if (perStock.Value > maxValue)
                        maxValue = perStock.Value;
                }
            }
            if (allHaveIt)
            {
                intersectionCount++;
                dollarWeightedNumerator += minValue;
            }
            dollarWeightedDenominator += maxValue;

            result.Rows.Add(
                new FundOverlapRow
                {
                    CommonStockId = stockId,
                    Ticker = ticker,
                    Name = name,
                    Slices = slices,
                    IsCommon = allHaveIt,
                    CombinedValue = combinedValue,
                }
            );
        }

        result.UnionPositionCount = allStockIds.Count;
        result.IntersectionPositionCount = intersectionCount;
        result.JaccardSimilarityPercent =
            allStockIds.Count > 0 ? (double)intersectionCount / allStockIds.Count * 100.0 : 0;
        result.DollarWeightedOverlapPercent =
            dollarWeightedDenominator > 0
                ? (double)dollarWeightedNumerator / dollarWeightedDenominator * 100.0
                : 0;

        result.Rows = result.Rows.OrderByDescending(r => r.CombinedValue).ToList();
        return result;
    }

    private class FundAggregate
    {
        public InstitutionalHolder Holder { get; set; }
        public Dictionary<Guid, FundStockAggregate> PerStock { get; set; }
    }

    private class FundStockAggregate
    {
        public Guid CommonStockId { get; set; }
        public string Ticker { get; set; }
        public string Name { get; set; }
        public long Shares { get; set; }
        public long Value { get; set; }
    }
}
