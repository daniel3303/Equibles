using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Repositories.Projections;

public static class InstitutionPortfolioSummaryCalculator
{
    public static InstitutionPortfolioSummary Calculate(
        IReadOnlyCollection<InstitutionalHolding> currentHoldings,
        IReadOnlyCollection<InstitutionalHolding> priorHoldings,
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

        if (currentHoldings.Count == 0)
            return summary;

        var currentByStock = AggregateByStock(currentHoldings);

        var aum = 0L;
        foreach (var kv in currentByStock)
            aum += kv.Value.Value;

        summary.ReportedAum = aum;
        summary.PositionCount = currentByStock.Count;

        if (aum > 0)
        {
            var valuesDescending = currentByStock
                .Values.Select(p => p.Value)
                .OrderByDescending(v => v)
                .ToList();
            summary.Top10ConcentrationPercent = ConcentrationPercent(valuesDescending, 10, aum);
            summary.Top25ConcentrationPercent = ConcentrationPercent(valuesDescending, 25, aum);
        }

        if (priorHoldings.Count == 0 || aum == 0)
            return summary;

        var priorByStock = AggregateByStock(priorHoldings);
        summary.QuarterOverQuarterTurnoverPercent = TurnoverPercent(
            currentByStock,
            priorByStock,
            aum
        );

        return summary;
    }

    private static Dictionary<Guid, (long Shares, long Value)> AggregateByStock(
        IReadOnlyCollection<InstitutionalHolding> holdings
    )
    {
        var byStock = new Dictionary<Guid, (long Shares, long Value)>();
        foreach (var holding in holdings)
        {
            byStock.TryGetValue(holding.CommonStockId, out var existing);
            byStock[holding.CommonStockId] = (
                existing.Shares + holding.Shares,
                existing.Value + holding.Value
            );
        }
        return byStock;
    }

    private static decimal ConcentrationPercent(
        IReadOnlyList<long> valuesDescending,
        int topN,
        long aum
    )
    {
        var top = 0L;
        var limit = Math.Min(topN, valuesDescending.Count);
        for (var i = 0; i < limit; i++)
            top += valuesDescending[i];
        return (decimal)top / aum * 100m;
    }

    private static decimal TurnoverPercent(
        IReadOnlyDictionary<Guid, (long Shares, long Value)> currentByStock,
        IReadOnlyDictionary<Guid, (long Shares, long Value)> priorByStock,
        long aum
    )
    {
        var stockIds = new HashSet<Guid>(currentByStock.Keys);
        foreach (var id in priorByStock.Keys)
            stockIds.Add(id);

        var dollarChange = 0m;
        foreach (var stockId in stockIds)
        {
            currentByStock.TryGetValue(stockId, out var cur);
            priorByStock.TryGetValue(stockId, out var prev);

            var sharesDelta = Math.Abs(cur.Shares - prev.Shares);
            if (sharesDelta == 0)
                continue;

            var pricePerShare = PricePerShare(cur, prev);
            dollarChange += sharesDelta * pricePerShare;
        }

        return dollarChange / (2m * aum) * 100m;
    }

    private static decimal PricePerShare(
        (long Shares, long Value) cur,
        (long Shares, long Value) prev
    )
    {
        if (cur.Shares > 0)
            return (decimal)cur.Value / cur.Shares;
        if (prev.Shares > 0)
            return (decimal)prev.Value / prev.Shares;
        return 0m;
    }
}
