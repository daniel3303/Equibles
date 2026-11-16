using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
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

        // Union of securities; build one row per security, with one slice per fund.
        var allSecurities = fundAggregates.SelectMany(f => f.PerStock.Keys).Distinct().ToList();

        long dollarWeightedNumerator = 0;
        long dollarWeightedDenominator = 0;
        var intersectionCount = 0;

        foreach (var security in allSecurities)
        {
            var (row, minValue, maxValue, allHaveIt) = BuildStockRow(security, fundAggregates);
            if (allHaveIt)
            {
                intersectionCount++;
                dollarWeightedNumerator += minValue;
            }
            dollarWeightedDenominator += maxValue;
            result.Rows.Add(row);
        }

        result.UnionPositionCount = allSecurities.Count;
        result.IntersectionPositionCount = intersectionCount;
        result.JaccardSimilarityPercent = Percentage.Of(intersectionCount, allSecurities.Count);
        result.DollarWeightedOverlapPercent = Percentage.Of(
            dollarWeightedNumerator,
            dollarWeightedDenominator
        );

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
                // Shared by presence, the rule consensus and the intersection read: a held
                // position whose value is pending or unavailable is still shared.
                if (!row.Slices[i].Held)
                    continue;
                for (var j = i; j < n; j++)
                {
                    if (!row.Slices[j].Held)
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
        (Guid CommonStockId, string ListedTicker) security,
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
            fund.PerStock.TryGetValue(security, out var perStock);
            ticker ??= perStock?.Ticker;
            name ??= perStock?.Name;
            var slice = new FundOverlapRowSlice
            {
                HolderId = fund.Holder.Id,
                Held = perStock != null,
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
            CommonStockId = security.CommonStockId,
            ListedTicker = security.ListedTicker,
            Ticker = ticker,
            Name = name,
            Slices = slices,
            IsCommon = allHaveIt,
            CombinedValue = combinedValue,
        };
        return (row, minValue, maxValue, allHaveIt);
    }

    // Aggregate each fund's positions per security. The aggregation collapses multiple
    // discretion rows for the same security so the per-fund slice is one entry per issuer
    // and listed ticker: a multi-series trust files every fund under one issuer.
    // A fund holds a security when its common-share rows for it report shares or a value. A put
    // or call row states the shares under the option at full notional value and a principal row
    // a debt amount, so a fund holding only those does not hold the stock, and a fund holding
    // shares and options shows its shares alone; each institution's own portfolio still lists
    // every row. Some filers list a position they left during the quarter with zero shares and
    // $0; that is no holding either, so it never counts toward consensus, the intersection or
    // the pairwise matrix. A position whose value is pending or unavailable still reports its
    // shares and counts. Portfolio weights divide by the fund's common-share total.
    private static FundAggregate BuildFundAggregate(
        (InstitutionalHolder Holder, IReadOnlyList<InstitutionalHolding> Holdings) fund
    )
    {
        var perStock = fund
            .Holdings.Where(h => h.ShareType == ShareType.Shares && h.OptionType == null)
            .GroupBy(h => (CommonStockId: h.EquityIssuerId, h.ListedTicker))
            .Select(g => new FundStockAggregate
            {
                CommonStockId = g.Key.CommonStockId,
                ListedTicker = g.Key.ListedTicker,
                Ticker = g.Key.ListedTicker ?? g.First().Issuer?.Presentation?.Listing?.Ticker,
                Name = ProductName(g.First().Issuer, g.Key.ListedTicker),
                Shares = g.Sum(h => h.Shares),
                Value = g.Sum(h => h.Value),
            })
            .Where(position => position.Shares > 0 || position.Value > 0)
            .ToDictionary(position => (position.CommonStockId, position.ListedTicker));
        return new FundAggregate
        {
            Holder = fund.Holder,
            PerStock = perStock,
            TotalValue = perStock.Values.Sum(s => s.Value),
        };
    }

    // A share class carries its issuer's name, but a trust's listed fund is its own product and
    // the trust's name can belong to a sibling fund, so such a row is left unnamed rather than
    // labelled with another product's name.
    private static string ProductName(EquityIssuer issuer, string listedTicker) =>
        listedTicker != null && SecondaryTickerPolicy.IsExchangeTradedListing(issuer, listedTicker)
            ? null
            : issuer?.Name;

    private class FundAggregate
    {
        public InstitutionalHolder Holder { get; set; }

        // Keyed by issuer and listed ticker; a null listed ticker is the primary listing.
        public Dictionary<
            (Guid CommonStockId, string ListedTicker),
            FundStockAggregate
        > PerStock { get; set; }
        public long TotalValue { get; set; }
    }

    private class FundStockAggregate
    {
        public Guid CommonStockId { get; set; }
        public string ListedTicker { get; set; }
        public string Ticker { get; set; }
        public string Name { get; set; }
        public long Shares { get; set; }
        public long Value { get; set; }
    }
}
