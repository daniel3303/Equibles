using Equibles.Holdings.Repositories.Models;

namespace Equibles.Holdings.Repositories;

public static class HoldingsBacktestCalculator
{
    // 13F filings are due 45 days after the quarter-end ReportDate; that's the earliest a
    // cloner could legally have learned the portfolio. Using ReportDate itself would peek.
    public const int RebalanceDelayDays = 45;

    // Bound memory and rendering cost — a 10y simulation already exceeds 3,600 daily points.
    public const int MaxYears = 10;

    public const decimal InitialValue = 100m;

    // `priceOf` is expected to forward-fill the last-known close so weekends, holidays, and
    // post-delisting dates still yield a non-null value. The calculator does not maintain
    // its own price cache; if priceOf returns null for a held stock on a given day, that
    // position's contribution is excluded that day.
    public static BacktestResult Calculate(
        IReadOnlyList<BacktestQuarterSnapshot> snapshots,
        DateOnly from,
        DateOnly to,
        Func<Guid, DateOnly, decimal?> priceOf,
        Func<DateOnly, decimal?> benchmarkPriceOf
    )
    {
        var result = new BacktestResult { StartDate = from, EndDate = to };

        if (from > to)
        {
            result.Reason = "from must be on or before to";
            return result;
        }

        var horizonCap = from.AddYears(MaxYears);
        if (to > horizonCap)
            to = horizonCap;
        result.EndDate = to;

        if (snapshots.Count == 0)
        {
            result.Reason = "no quarterly snapshots available";
            return result;
        }

        var ordered = snapshots
            .Select(s => (Snapshot: s, RebalanceDate: s.ReportDate.AddDays(RebalanceDelayDays)))
            .OrderBy(x => x.RebalanceDate)
            .ToList();

        // First snapshot whose rebalance date is on or after `from`. If none — the window
        // sits after the latest filing; fall back to the most recent prior snapshot so the
        // simulation still has a portfolio to mark to market.
        var firstIdx = ordered.FindIndex(x => x.RebalanceDate >= from);
        var snapshotIdx = firstIdx < 0 ? ordered.Count - 1 : firstIdx;

        var startDate = ordered[snapshotIdx].RebalanceDate;
        if (startDate < from)
            startDate = from;
        if (startDate > to)
        {
            result.Reason = "no rebalance date falls inside the requested window";
            return result;
        }
        result.StartDate = startDate;

        var benchStart = benchmarkPriceOf(startDate);
        if (benchStart is null || benchStart.Value <= 0)
        {
            result.Reason = $"no benchmark price at {startDate:yyyy-MM-dd}";
            return result;
        }

        var holdings = new Dictionary<Guid, decimal>();
        var portfolioValue = InitialValue;

        Rebalance(holdings, ordered[snapshotIdx].Snapshot, startDate, portfolioValue, priceOf);

        for (var day = startDate; day <= to; day = day.AddDays(1))
        {
            // Advance through any rebalance dates that fall on/before `day`. Mark to market
            // with the prior holdings first so the rebalance uses an honest portfolio value.
            while (snapshotIdx + 1 < ordered.Count && ordered[snapshotIdx + 1].RebalanceDate <= day)
            {
                snapshotIdx++;
                portfolioValue = MarkToMarket(holdings, day, priceOf, portfolioValue);
                Rebalance(holdings, ordered[snapshotIdx].Snapshot, day, portfolioValue, priceOf);
            }

            portfolioValue = MarkToMarket(holdings, day, priceOf, portfolioValue);

            var benchPriceToday = benchmarkPriceOf(day) ?? benchStart.Value;
            var benchValue = InitialValue * (benchPriceToday / benchStart.Value);

            result.Points.Add(
                new BacktestPoint
                {
                    Date = day,
                    PortfolioValue = Math.Round(portfolioValue, 4),
                    BenchmarkValue = Math.Round(benchValue, 4),
                }
            );
        }

        if (result.Points.Count > 0)
        {
            result.PortfolioSummary = ComputeSummary(result.Points.Select(p => p.PortfolioValue));
            result.BenchmarkSummary = ComputeSummary(result.Points.Select(p => p.BenchmarkValue));
        }

        return result;
    }

    private static void Rebalance(
        Dictionary<Guid, decimal> holdings,
        BacktestQuarterSnapshot snapshot,
        DateOnly date,
        decimal currentValue,
        Func<Guid, DateOnly, decimal?> priceOf
    )
    {
        holdings.Clear();
        if (currentValue <= 0)
            return;

        // Aggregate per stock — a holder may report a single stock across multiple rows
        // when multiple managers share discretion. Options rows are notional and skipped.
        var positions = snapshot
            .Positions.Where(p => !p.IsOption && p.Value > 0)
            .GroupBy(p => p.CommonStockId)
            .Select(g => (StockId: g.Key, Value: g.Sum(p => p.Value)))
            .ToList();
        var totalValue = positions.Sum(p => p.Value);
        if (totalValue <= 0)
            return;

        foreach (var (stockId, value) in positions)
        {
            var price = priceOf(stockId, date);
            if (price is null || price.Value <= 0)
                continue;
            var weight = (decimal)value / totalValue;
            var allocation = currentValue * weight;
            holdings[stockId] = allocation / price.Value;
        }
    }

    private static decimal MarkToMarket(
        Dictionary<Guid, decimal> holdings,
        DateOnly date,
        Func<Guid, DateOnly, decimal?> priceOf,
        decimal fallback
    )
    {
        if (holdings.Count == 0)
            return fallback;
        decimal sum = 0;
        foreach (var (stockId, shares) in holdings)
        {
            var price = priceOf(stockId, date);
            if (price is null || price.Value <= 0)
                continue;
            sum += shares * price.Value;
        }
        return sum > 0 ? sum : fallback;
    }

    private static BacktestStrategySummary ComputeSummary(IEnumerable<decimal> series)
    {
        var values = series.ToList();
        if (values.Count == 0)
            return new BacktestStrategySummary();
        var initial = values[0];
        var final = values[^1];
        if (initial <= 0)
            return new BacktestStrategySummary();

        var totalReturn = (final / initial - 1m) * 100m;

        var days = (decimal)(values.Count - 1);
        decimal cagr = 0m;
        if (days > 0 && final > 0)
        {
            var years = (double)days / 365.25;
            var ratio = (double)(final / initial);
            cagr = (decimal)(Math.Pow(ratio, 1.0 / years) - 1.0) * 100m;
        }

        decimal peak = values[0];
        decimal maxDrawdown = 0m;
        foreach (var v in values)
        {
            if (v > peak)
                peak = v;
            if (peak > 0)
            {
                var dd = (peak - v) / peak * 100m;
                if (dd > maxDrawdown)
                    maxDrawdown = dd;
            }
        }

        return new BacktestStrategySummary
        {
            TotalReturnPercent = Math.Round(totalReturn, 2),
            CagrPercent = Math.Round(cagr, 2),
            MaxDrawdownPercent = Math.Round(maxDrawdown, 2),
        };
    }
}
