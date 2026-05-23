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

        var fundAggregates = funds.Select(BuildFundAggregate).ToList();

        foreach (var fund in fundAggregates)
        {
            result.Funds.Add(
                new FundOverlapFund
                {
                    HolderId = fund.Holder.Id,
                    HolderCik = fund.Holder.Cik,
                    HolderName = fund.Holder.Name,
                    PositionCount = fund.PerStock.Count,
                    TotalValue = fund.TotalValue,
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
            var (row, minValue, maxValue, allHaveIt) = BuildStockRow(stockId, fundAggregates);
            if (allHaveIt)
            {
                intersectionCount++;
                dollarWeightedNumerator += minValue;
            }
            dollarWeightedDenominator += maxValue;
            result.Rows.Add(row);
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

    public static PairwiseOverlapMatrix ComputePairwiseOverlap(FundOverlapResult overlap)
    {
        var n = overlap.Funds.Count;
        var matrix = new int[n][];
        for (var i = 0; i < n; i++)
            matrix[i] = new int[n];

        foreach (var row in overlap.Rows)
        {
            for (var i = 0; i < n; i++)
            {
                if (row.Slices[i].Value <= 0)
                    continue;
                for (var j = i; j < n; j++)
                {
                    if (row.Slices[j].Value <= 0)
                        continue;
                    matrix[i][j]++;
                    if (i != j)
                        matrix[j][i]++;
                }
            }
        }

        return new PairwiseOverlapMatrix
        {
            ReportDate = overlap.ReportDate,
            Funds = overlap.Funds,
            SharedTickerCounts = matrix,
        };
    }

    private static (FundOverlapRow Row, long MinValue, long MaxValue, bool AllHaveIt) BuildStockRow(
        Guid stockId,
        List<FundAggregate> fundAggregates
    )
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
            var slice = new FundOverlapRowSlice
            {
                HolderId = fund.Holder.Id,
                Shares = perStock?.Shares ?? 0,
                Value = perStock?.Value ?? 0,
                PercentOfPortfolio =
                    fund.TotalValue > 0 && perStock != null
                        ? (double)perStock.Value / fund.TotalValue * 100.0
                        : 0,
            };
            slices.Add(slice);
            combinedValue += slice.Value;
            if (perStock == null)
            {
                allHaveIt = false;
            }
            else
            {
                minValue = Math.Min(minValue, perStock.Value);
                maxValue = Math.Max(maxValue, perStock.Value);
            }
        }

        var row = new FundOverlapRow
        {
            CommonStockId = stockId,
            Ticker = ticker,
            Name = name,
            Slices = slices,
            IsCommon = allHaveIt,
            CombinedValue = combinedValue,
        };
        return (row, minValue, maxValue, allHaveIt);
    }

    // Aggregate each fund's positions per stock. The aggregation collapses multiple
    // discretion rows for the same stock so the per-fund slice is one entry per
    // CommonStockId.
    private static FundAggregate BuildFundAggregate(
        (InstitutionalHolder Holder, IReadOnlyList<InstitutionalHolding> Holdings) fund
    )
    {
        var perStock = fund
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
        return new FundAggregate
        {
            Holder = fund.Holder,
            PerStock = perStock,
            TotalValue = perStock.Values.Sum(s => s.Value),
        };
    }

    private class FundAggregate
    {
        public InstitutionalHolder Holder { get; set; }
        public Dictionary<Guid, FundStockAggregate> PerStock { get; set; }
        public long TotalValue { get; set; }
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
