using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.Holdings.Repositories;

public static class InstitutionPortfolioSummaryCalculator
{
    public static InstitutionPortfolioSummary Calculate(
        IReadOnlyList<InstitutionalHolding> currentQuarterHoldings,
        IReadOnlyList<InstitutionalHolding> previousQuarterHoldings,
        int quartersReported,
        DateOnly? latestReportDate,
        DateOnly? previousReportDate
    )
    {
        var summary = new InstitutionPortfolioSummary
        {
            QuartersReported = quartersReported,
            LatestReportDate = latestReportDate,
            PreviousReportDate = previousReportDate,
        };

        if (currentQuarterHoldings.Count == 0)
            return summary;

        // Aggregate per stock — a filer may report a stock across multiple rows when
        // multiple managers share discretion; only the aggregated per-stock figures
        // are meaningful for AUM / concentration / turnover.
        var byStock = currentQuarterHoldings
            .GroupBy(h => h.CommonStockId)
            .Select(g => new { Shares = g.Sum(h => h.Shares), Value = g.Sum(h => h.Value) })
            .ToList();

        summary.ReportedAum = byStock.Sum(p => p.Value);
        summary.PositionCount = byStock.Count;

        var valuesDesc = byStock.OrderByDescending(p => p.Value).Select(p => p.Value).ToList();
        if (summary.ReportedAum > 0)
        {
            summary.Top10ConcentrationPercent =
                (double)valuesDesc.Take(10).Sum() / summary.ReportedAum * 100.0;
            summary.Top25ConcentrationPercent =
                (double)valuesDesc.Take(25).Sum() / summary.ReportedAum * 100.0;
        }

        if (previousQuarterHoldings.Count > 0 && summary.ReportedAum > 0)
        {
            // Current-quarter price proxy = Value / Shares per stock. For each stock that
            // appears in either quarter, |Δ shares × current price proxy| is the absolute
            // dollar movement; the canonical turnover formula then divides by 2 × AUM.
            var currentByStock = currentQuarterHoldings
                .GroupBy(h => h.CommonStockId)
                .ToDictionary(
                    g => g.Key,
                    g => new { Shares = g.Sum(h => h.Shares), Value = g.Sum(h => h.Value) }
                );
            var previousByStock = previousQuarterHoldings
                .GroupBy(h => h.CommonStockId)
                .ToDictionary(g => g.Key, g => g.Sum(h => h.Shares));

            var allStockIds = currentByStock.Keys.Union(previousByStock.Keys);
            decimal turnoverDollars = 0m;
            foreach (var stockId in allStockIds)
            {
                currentByStock.TryGetValue(stockId, out var current);
                previousByStock.TryGetValue(stockId, out var priorShares);
                var currentShares = current?.Shares ?? 0;
                var deltaShares = Math.Abs(currentShares - priorShares);
                if (deltaShares == 0)
                    continue;

                // Per-share proxy from the current quarter; fall back to 0 when the
                // stock was sold out (no current Value to derive a proxy from). The
                // sold-out side of the turnover for that stock is unavoidably missed
                // without a price-history dependency, accepting that limitation.
                var perShare = current is { Shares: > 0 }
                    ? (decimal)current.Value / current.Shares
                    : 0m;
                turnoverDollars += deltaShares * perShare;
            }
            summary.QoQTurnoverPercent =
                (double)(turnoverDollars / (2m * summary.ReportedAum)) * 100.0;
        }

        return summary;
    }
}
