using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Finra.BusinessLogic.Models;
using Equibles.Finra.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Finra.BusinessLogic;

/// <summary>
/// Computes a composite 0–100 short-squeeze score per stock from data already stored —
/// no scraper and no per-ticker heuristics. Three factors, each normalized to a
/// percentile across the scored universe so the score is peer-relative:
/// <list type="bullet">
/// <item>short interest as a fraction of shares outstanding,</item>
/// <item>days-to-cover (FINRA-reported, or short position ÷ average daily volume),</item>
/// <item>the change in the short share of total volume between the last two weeks and
/// the two before them (rising = pressure building).</item>
/// </list>
/// The composite is the equal-weight mean of the factor percentiles a stock has data
/// for — weights are deliberate and documented rather than tuned. The universe is every
/// stock reporting short interest at the latest settlement date with a known
/// shares-outstanding count.
/// </summary>
[Service]
public class ShortSqueezeScoreManager
{
    /// <summary>Each trend window pools two calendar weeks of daily short-volume rows.</summary>
    public const int TrendWindowDays = 14;

    private readonly ShortInterestRepository _shortInterestRepository;
    private readonly DailyShortVolumeRepository _dailyShortVolumeRepository;
    private readonly CommonStockRepository _commonStockRepository;

    public ShortSqueezeScoreManager(
        ShortInterestRepository shortInterestRepository,
        DailyShortVolumeRepository dailyShortVolumeRepository,
        CommonStockRepository commonStockRepository
    )
    {
        _shortInterestRepository = shortInterestRepository;
        _dailyShortVolumeRepository = dailyShortVolumeRepository;
        _commonStockRepository = commonStockRepository;
    }

    public async Task<List<ShortSqueezeScore>> Compute(
        CancellationToken cancellationToken = default
    )
    {
        var settlementDate = await _shortInterestRepository
            .GetLatestSettlementDate()
            .FirstOrDefaultAsync(cancellationToken);
        if (settlementDate == default)
        {
            return [];
        }

        var shortInterests = await _shortInterestRepository
            .GetBySettlementDate(settlementDate)
            .ToListAsync(cancellationToken);

        var stockIds = shortInterests.Select(s => s.CommonStockId).Distinct().ToList();
        var stocks = await _commonStockRepository
            .GetAll()
            .Where(s => stockIds.Contains(s.Id) && s.SharesOutStanding > 0)
            .Select(s => new
            {
                s.Id,
                s.Ticker,
                s.SharesOutStanding,
            })
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var trends = await LoadShortVolumeTrends(stockIds, settlementDate, cancellationToken);

        var scores = new List<ShortSqueezeScore>();
        foreach (var shortInterest in shortInterests)
        {
            if (!stocks.TryGetValue(shortInterest.CommonStockId, out var stock))
            {
                continue;
            }

            var daysToCover = shortInterest.DaysToCover;
            if (shortInterest.AverageDailyVolume == 0)
            {
                // FINRA reports days-to-cover as a 1000.0 sentinel when a listing has
                // zero average daily volume — a division-by-zero placeholder, not a
                // measurement. Drop the factor so untradeable shells aren't ranked as
                // top squeeze risk on the sentinel alone.
                daysToCover = null;
            }
            else if (daysToCover == null && shortInterest.AverageDailyVolume > 0)
            {
                daysToCover =
                    shortInterest.CurrentShortPosition
                    / (decimal)shortInterest.AverageDailyVolume.Value;
            }

            scores.Add(
                new ShortSqueezeScore
                {
                    CommonStockId = stock.Id,
                    Ticker = stock.Ticker,
                    SettlementDate = settlementDate,
                    ShortInterestPercentOfShares =
                        shortInterest.CurrentShortPosition / (decimal)stock.SharesOutStanding,
                    DaysToCover = daysToCover,
                    // TryGetValue, not GetValueOrDefault: a stock with no volume data
                    // must carry a null trend (factor drops out), never a zero trend.
                    ShortVolumeShareTrend = trends.TryGetValue(stock.Id, out var trend)
                        ? trend
                        : null,
                }
            );
        }

        ApplyPercentiles(scores);
        return scores.OrderByDescending(s => s.Score).ToList();
    }

    /// <summary>
    /// Pools each stock's daily short-volume rows (summed across venues) into two
    /// adjacent calendar windows ending at the settlement date and returns the change
    /// in the pooled short-volume ratio. Stocks lacking volume in either window get no
    /// trend — the factor drops out of their composite rather than defaulting.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> LoadShortVolumeTrends(
        List<Guid> stockIds,
        DateOnly settlementDate,
        CancellationToken cancellationToken
    )
    {
        var midCutoff = settlementDate.AddDays(-TrendWindowDays);
        var farCutoff = settlementDate.AddDays(-TrendWindowDays * 2);

        var windows = await _dailyShortVolumeRepository
            .GetAll()
            .Where(v =>
                v.Date > farCutoff && v.Date <= settlementDate && stockIds.Contains(v.CommonStockId)
            )
            .GroupBy(v => new { v.CommonStockId, Recent = v.Date > midCutoff })
            .Select(g => new
            {
                g.Key.CommonStockId,
                g.Key.Recent,
                ShortVolume = g.Sum(v => v.ShortVolume),
                TotalVolume = g.Sum(v => v.TotalVolume),
            })
            .ToListAsync(cancellationToken);

        var trends = new Dictionary<Guid, decimal>();
        foreach (var group in windows.GroupBy(w => w.CommonStockId))
        {
            var recent = group.FirstOrDefault(w => w.Recent);
            var prior = group.FirstOrDefault(w => !w.Recent);
            if (recent is not { TotalVolume: > 0 } || prior is not { TotalVolume: > 0 })
            {
                continue;
            }

            trends[group.Key] =
                recent.ShortVolume / (decimal)recent.TotalVolume
                - prior.ShortVolume / (decimal)prior.TotalVolume;
        }

        return trends;
    }

    private static void ApplyPercentiles(List<ShortSqueezeScore> scores)
    {
        var shortInterestPercentiles = Percentiles(
            scores.Select(s => (s.CommonStockId, s.ShortInterestPercentOfShares))
        );
        var daysToCoverPercentiles = Percentiles(
            scores
                .Where(s => s.DaysToCover != null)
                .Select(s => (s.CommonStockId, s.DaysToCover.Value))
        );
        var trendPercentiles = Percentiles(
            scores
                .Where(s => s.ShortVolumeShareTrend != null)
                .Select(s => (s.CommonStockId, s.ShortVolumeShareTrend.Value))
        );

        foreach (var score in scores)
        {
            score.ShortInterestPercentile = shortInterestPercentiles[score.CommonStockId];
            score.DaysToCoverPercentile = daysToCoverPercentiles.TryGetValue(
                score.CommonStockId,
                out var daysToCover
            )
                ? daysToCover
                : null;
            score.ShortVolumeTrendPercentile = trendPercentiles.TryGetValue(
                score.CommonStockId,
                out var trend
            )
                ? trend
                : null;

            double total = score.ShortInterestPercentile;
            var factors = 1;
            if (score.DaysToCoverPercentile != null)
            {
                total += score.DaysToCoverPercentile.Value;
                factors++;
            }

            if (score.ShortVolumeTrendPercentile != null)
            {
                total += score.ShortVolumeTrendPercentile.Value;
                factors++;
            }

            score.Score = Math.Round(total / factors, 2);
        }
    }

    /// <summary>
    /// Average-rank percentiles on a 0–100 scale: ties share the mean of the ranks
    /// they span, a single-entry set sits at 50, and the extremes map to 0 and 100 —
    /// the standard peer-relative normalization.
    /// </summary>
    private static Dictionary<Guid, double> Percentiles(
        IEnumerable<(Guid Id, decimal Value)> values
    )
    {
        var entries = values.ToList();
        var result = new Dictionary<Guid, double>(entries.Count);
        if (entries.Count == 0)
        {
            return result;
        }

        if (entries.Count == 1)
        {
            result[entries[0].Id] = 50;
            return result;
        }

        var ordered = entries.OrderBy(e => e.Value).ToList();
        var index = 0;
        while (index < ordered.Count)
        {
            var tieEnd = index;
            while (tieEnd + 1 < ordered.Count && ordered[tieEnd + 1].Value == ordered[index].Value)
            {
                tieEnd++;
            }

            var averageRank = (index + tieEnd) / 2.0;
            var percentile = averageRank / (ordered.Count - 1) * 100;
            for (var i = index; i <= tieEnd; i++)
            {
                result[ordered[i].Id] = percentile;
            }

            index = tieEnd + 1;
        }

        return result;
    }
}
