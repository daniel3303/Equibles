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
        var current = currentQuarterHoldings
            .GroupBy(h => h.CommonStockId)
            .Select(g => new InstitutionPortfolioPosition
            {
                CommonStockId = g.Key,
                Shares = g.Sum(h => h.Shares),
                Value = g.Sum(h => h.Value),
            })
            .ToList();
        var previous = previousQuarterHoldings
            .GroupBy(h => h.CommonStockId)
            .Select(g => new InstitutionPortfolioPosition
            {
                CommonStockId = g.Key,
                Shares = g.Sum(h => h.Shares),
                Value = g.Sum(h => h.Value),
            })
            .ToList();
        return CalculatePositions(
            current,
            previous,
            quartersReported,
            latestReportDate,
            previousReportDate
        );
    }

    public static InstitutionPortfolioSummary CalculatePositions(
        IReadOnlyList<InstitutionPortfolioPosition> currentQuarterPositions,
        IReadOnlyList<InstitutionPortfolioPosition> previousQuarterPositions,
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

        if (currentQuarterPositions.Count == 0)
            return summary;

        summary.ReportedAum = currentQuarterPositions.Sum(p => p.Value);
        summary.PositionCount = currentQuarterPositions.Count;

        var valuesDesc = currentQuarterPositions
            .OrderByDescending(p => p.Value)
            .Select(p => p.Value)
            .ToList();
        if (summary.ReportedAum > 0)
        {
            summary.Top10ConcentrationPercent =
                (double)valuesDesc.Take(10).Sum() / summary.ReportedAum * 100.0;
            summary.Top25ConcentrationPercent =
                (double)valuesDesc.Take(25).Sum() / summary.ReportedAum * 100.0;
        }

        if (previousQuarterPositions.Count > 0 && summary.ReportedAum > 0)
        {
            summary.QoQTurnoverPercent = ComputeQoQTurnoverPercent(
                currentQuarterPositions,
                previousQuarterPositions,
                summary.ReportedAum
            );
        }

        return summary;
    }

    private static double ComputeQoQTurnoverPercent(
        IReadOnlyList<InstitutionPortfolioPosition> currentQuarterPositions,
        IReadOnlyList<InstitutionPortfolioPosition> previousQuarterPositions,
        long reportedAum
    )
    {
        // Current-quarter price proxy = Value / Shares per stock. For each stock that
        // appears in either quarter, |Δ shares × current price proxy| is the absolute
        // dollar movement; the canonical turnover formula then divides by 2 × AUM.
        var currentByStock = currentQuarterPositions.ToDictionary(p => p.CommonStockId);
        var previousByStock = previousQuarterPositions.ToDictionary(
            p => p.CommonStockId,
            p => p.Shares
        );

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
        return (double)(turnoverDollars / (2m * reportedAum)) * 100.0;
    }
}
