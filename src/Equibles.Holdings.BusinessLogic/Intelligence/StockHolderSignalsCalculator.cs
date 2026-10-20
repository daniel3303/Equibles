using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// Folds every filer's book into per-security totals one filer at a time, then publishes one
/// <see cref="StockQuarterlyHolderSignals"/> row per security held. Only running sums, the weights
/// behind the median and each security's ten largest holdings are kept, never a filer's positions.
/// </summary>
public sealed class StockHolderSignalsCalculator
{
    /// <summary>Tenure under this many quarters is short.</summary>
    public const int MediumTenureQuarters = 4;

    /// <summary>Tenure of at least this many quarters is long.</summary>
    public const int LongTenureQuarters = 12;

    /// <summary>A weight at or above this share of a filer's book is a high-conviction position.</summary>
    public const decimal HighWeight = 0.05m;

    /// <summary>A security among a filer's this many largest issuers is a conviction holding.</summary>
    public const int ConvictionRank = HolderConcentrationCalculator.TopPositions;

    private const int TopHolders = 10;

    private readonly Dictionary<HolderSecurityKey, Totals> _totals = new();

    /// <summary>The style bands whose like-for-like change counts as active holders' change.</summary>
    public static bool IsActiveStyle(HolderStyleBand band) =>
        band
            is HolderStyleBand.SystematicLike
                or HolderStyleBand.Concentrated
                or HolderStyleBand.DiversifiedActive;

    public void Add(HolderSignalsContribution holder)
    {
        var book = holder.Book;
        var current = Positions(book.Current);
        var topIssuers = HolderConcentrationCalculator
            .IssuerValues(book.Current)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(ConvictionRank)
            .Select(pair => pair.Key)
            .ToHashSet();
        var crowded = holder.TurnoverBand == HolderTurnoverBand.High || holder.AdvisesPrivateFunds;

        foreach (var (security, position) in current)
        {
            var totals = For(security);
            totals.Filers++;
            totals.Shares += position.Shares;
            totals.TopHolderShares.Enqueue(position.Shares, position.Shares);
            if (totals.TopHolderShares.Count > TopHolders)
                totals.TopHolderShares.Dequeue();
            totals.SumOfSquares += (double)position.Shares * position.Shares;
            if (position.Value is { } value)
            {
                totals.Value += value;
                totals.ValuedShares += position.Shares;
                if (holder.EquityAum > 0)
                {
                    var weight = (decimal)value / holder.EquityAum;
                    totals.Weights.Add((double)weight);
                    totals.WeightSum += weight;
                    if (weight >= HighWeight)
                        totals.HoldersAboveFivePercent++;
                }
            }
            if (topIssuers.Contains(security.IssuerId))
                totals.ConvictionHolders++;
            if (crowded)
            {
                totals.CrowdedShares += position.Shares;
                totals.CrowdedHolders++;
            }
            if (holder.AdvisesPrivateFunds)
                totals.PrivateFundAdviserShares += position.Shares;

            var valued = position.Value.GetValueOrDefault();
            if (valued > 0 && book.Tenures.TryGetValue(security, out var tenure))
            {
                totals.TenureValue += valued;
                totals.TenureWeighted += (decimal)valued * tenure.Quarters;
                if (tenure.Quarters >= LongTenureQuarters)
                    totals.TenureLongValue += valued;
                else if (tenure.Censored)
                    totals.TenureCensoredValue += valued;
                else if (tenure.Quarters >= MediumTenureQuarters)
                    totals.TenureMediumValue += valued;
                else
                    totals.TenureShortValue += valued;
            }

            if (
                book.CostSlots.TryGetValue(security, out var slot)
                && slot.Status == HolderCostStatus.Estimated
                && slot.CostPerShare is decimal cost
                && cost > 0
            )
            {
                totals.PricedShares += position.Shares;
                totals.CostWeighted += cost * position.Shares;
                if (valued > 0)
                {
                    totals.PricedValue += valued;
                    if (cost * position.Shares > valued)
                        totals.UnderwaterValue += valued;
                }
            }
        }

        if (book.Previous != null && IsActiveStyle(holder.StyleBand))
        {
            var previous = Positions(book.Previous);
            foreach (var security in current.Keys.Union(previous.Keys))
            {
                if (book.UnresolvedBasis.Contains(security))
                    continue;
                var totals = For(security);
                totals.ActiveCompared = true;
                totals.ActiveCurrent += current.GetValueOrDefault(security)?.Shares ?? 0;
                totals.ActivePrevious += previous.GetValueOrDefault(security)?.Shares ?? 0;
            }
        }

        if (holder.IsTopScored)
            foreach (var trade in book.SecurityTrades)
            {
                var totals = For(trade.Security);
                totals.TopScoreNet += trade.BuyValue - trade.SellValue;
                if (trade.BuyValue > 0)
                    totals.TopScoreBuyers++;
                if (trade.SellValue > 0)
                    totals.TopScoreSellers++;
            }
    }

    /// <summary>One row per security some filer holds this quarter.</summary>
    public List<StockQuarterlyHolderSignals> Build(
        DateOnly reportDate,
        StockHolderSignalsQuarterInputs inputs
    )
    {
        var rows = new List<StockQuarterlyHolderSignals>(_totals.Count);
        foreach (var (security, totals) in _totals)
        {
            if (totals.Filers == 0)
                continue;
            var shares = inputs.SharesOutstanding.TryGetValue(security.IssuerId, out var count)
                ? count
                : ((long, DateOnly, SharesOutstandingBasis)?)null;
            var counts = inputs.BeneficialOwnership.GetValueOrDefault(security);
            rows.Add(
                new StockQuarterlyHolderSignals
                {
                    EquityIssuerId = security.IssuerId,
                    ListedTicker = security.ListedTicker,
                    ReportDate = reportDate,
                    Filers = totals.Filers,
                    Shares = totals.Shares,
                    Value = totals.Value,
                    ValuedShares = totals.ValuedShares,
                    ConvictionHolders = totals.ConvictionHolders,
                    HoldersAboveFivePercent = totals.HoldersAboveFivePercent,
                    AverageWeight =
                        totals.Weights.Count > 0 ? totals.WeightSum / totals.Weights.Count : null,
                    MedianWeight = Median(totals.Weights),
                    CrowdedShares = totals.CrowdedShares,
                    CrowdedHolders = totals.CrowdedHolders,
                    PrivateFundAdviserShares = totals.PrivateFundAdviserShares,
                    TenureShortValue = totals.TenureShortValue,
                    TenureMediumValue = totals.TenureMediumValue,
                    TenureLongValue = totals.TenureLongValue,
                    TenureCensoredValue = totals.TenureCensoredValue,
                    ValueWeightedTenureQuarters =
                        totals.TenureValue > 0 ? totals.TenureWeighted / totals.TenureValue : null,
                    Herfindahl =
                        totals.Shares > 0
                            ? (decimal)(
                                totals.SumOfSquares / ((double)totals.Shares * totals.Shares)
                            )
                            : null,
                    TopTenHolderShare =
                        totals.Shares > 0
                            ? (decimal)
                                totals.TopHolderShares.UnorderedItems.Sum(item => item.Element)
                                / totals.Shares
                            : null,
                    PricedShares = totals.PricedShares,
                    PricedValue = totals.PricedValue,
                    EstimatedCostPerShare =
                        totals.PricedShares > 0 ? totals.CostWeighted / totals.PricedShares : null,
                    UnderwaterValueShare =
                        totals.PricedValue > 0
                            ? (decimal)totals.UnderwaterValue / totals.PricedValue
                            : null,
                    ActiveLikeForLikeShares = totals.ActiveCompared ? totals.ActiveCurrent : null,
                    ActiveLikeForLikePreviousShares = totals.ActiveCompared
                        ? totals.ActivePrevious
                        : null,
                    TopScoreNetBuyValue = inputs.MeasuresTopScored ? totals.TopScoreNet : null,
                    TopScoreBuyers = inputs.MeasuresTopScored ? totals.TopScoreBuyers : null,
                    TopScoreSellers = inputs.MeasuresTopScored ? totals.TopScoreSellers : null,
                    SharesOutstanding = shares?.Item1,
                    SharesOutstandingAsOf = shares?.Item2,
                    SharesOutstandingBasis = shares?.Item3 ?? SharesOutstandingBasis.None,
                    InsiderShares =
                        security.ListedTicker == null
                        && inputs.InsiderShares.TryGetValue(security.IssuerId, out var insiders)
                            ? insiders
                            : null,
                    Schedule13DFilers = counts?.Schedule13D ?? 0,
                    Schedule13GFilers = counts?.Schedule13G ?? 0,
                    Schedule13GQualifiedInstitutions = counts?.QualifiedInstitutions ?? 0,
                    Schedule13GPassiveInvestors = counts?.PassiveInvestors ?? 0,
                    Schedule13GExemptInvestors = counts?.ExemptInvestors ?? 0,
                }
            );
        }
        RankCrowding(rows);
        return rows;
    }

    /// <summary>
    /// Whether a row's shares outstanding can divide its own holders: the primary listing of an
    /// issuer whose count is one consolidated figure in the listed unit.
    /// </summary>
    public static bool CountDividesListing(StockQuarterlyHolderSignals row) =>
        row.ListedTicker == null
        && row.SharesOutstandingBasis == SharesOutstandingBasis.Consolidated
        && row.SharesOutstanding > 0;

    // Crowded shares over shares outstanding, as the percentile among the quarter's rows a count
    // can divide; ties share the highest rank.
    private static void RankCrowding(List<StockQuarterlyHolderSignals> rows)
    {
        var measured = rows.Where(CountDividesListing)
            .Select(row =>
                (Row: row, Ratio: (decimal)row.CrowdedShares / row.SharesOutstanding.Value)
            )
            .OrderBy(pair => pair.Ratio)
            .ToList();
        if (measured.Count == 0)
            return;
        var index = 0;
        while (index < measured.Count)
        {
            var end = index;
            while (end + 1 < measured.Count && measured[end + 1].Ratio == measured[index].Ratio)
                end++;
            var percentile = Math.Round(100m * (end + 1) / measured.Count, 4);
            for (var i = index; i <= end; i++)
                measured[i].Row.CrowdingPercentile = percentile;
            index = end + 1;
        }
    }

    private static decimal? Median(List<double> weights)
    {
        if (weights.Count == 0)
            return null;
        weights.Sort();
        var middle = weights.Count / 2;
        var median =
            weights.Count % 2 == 1 ? weights[middle] : (weights[middle - 1] + weights[middle]) / 2;
        return (decimal)median;
    }

    private Totals For(HolderSecurityKey security)
    {
        if (!_totals.TryGetValue(security, out var totals))
        {
            totals = new Totals();
            _totals[security] = totals;
        }
        return totals;
    }

    // One security's rows of a book summed: a value pending on any row leaves it unvalued.
    private static Dictionary<HolderSecurityKey, HolderEquityPosition> Positions(
        IReadOnlyList<HolderEquityPosition> positions
    ) =>
        positions
            .Where(position => position.Shares > 0)
            .GroupBy(position => position.Security)
            .ToDictionary(
                group => group.Key,
                group => new HolderEquityPosition(
                    group.Key,
                    group.Sum(position => position.Shares),
                    group.All(position => position.Value != null)
                        ? group.Sum(position => position.Value.GetValueOrDefault())
                        : null
                )
            );

    private sealed class Totals
    {
        public int Filers;
        public long Shares;
        public long Value;
        public long ValuedShares;
        public int ConvictionHolders;
        public int HoldersAboveFivePercent;
        public readonly List<double> Weights = [];
        public decimal WeightSum;
        public long CrowdedShares;
        public int CrowdedHolders;
        public long PrivateFundAdviserShares;
        public long TenureShortValue;
        public long TenureMediumValue;
        public long TenureLongValue;
        public long TenureCensoredValue;
        public long TenureValue;
        public decimal TenureWeighted;
        public double SumOfSquares;
        public readonly PriorityQueue<long, long> TopHolderShares = new();
        public long PricedShares;
        public long PricedValue;
        public decimal CostWeighted;
        public long UnderwaterValue;
        public bool ActiveCompared;
        public long ActiveCurrent;
        public long ActivePrevious;
        public decimal TopScoreNet;
        public int TopScoreBuyers;
        public int TopScoreSellers;
    }
}
