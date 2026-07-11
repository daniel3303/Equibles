using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Finra.BusinessLogic.Models;
using Equibles.Finra.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Finra.BusinessLogic;

/// <summary>
/// Computes a composite 0–100 short-squeeze score per stock from data already stored —
/// no scraper and no per-ticker heuristics. The construction follows the structure of
/// the published squeeze models (IHS Markit's US Short Squeeze Model most directly):
/// a set of crowdedness / capital-constraint factors, each normalized to a percentile
/// across the scored universe and combined as a weighted mean, plus additive catalyst
/// boosts for event conditions that ignite squeezes.
///
/// <para>Crowdedness factors (weight — higher percentile = more squeeze-prone):</para>
/// <list type="bullet">
/// <item>short interest as a fraction of shares outstanding (30%),</item>
/// <item>days-to-cover — FINRA-reported, or short position ÷ average daily volume (20%),</item>
/// <item>price versus trailing VWAP — how far open shorts are underwater, the public-data
/// analog of transaction-level out-of-the-money share measures (15%),</item>
/// <item>the change in the short share of total volume between the last two weeks and
/// the two before them — rising = pressure building (15%),</item>
/// <item>the change in the short position versus the previous FINRA report — the crowd
/// is still building (10%),</item>
/// <item>fails-to-deliver pressure — the worst trailing single-day FTD quantity as a
/// fraction of shares outstanding, the closest public proxy for hard-to-borrow
/// conditions (10%).</item>
/// </list>
///
/// <para>Catalyst boosts (additive rank points, capped at <see cref="MaxCatalystBoost"/>,
/// composite clamped to 100): a statistically extreme positive weekly return
/// (+<see cref="PriceSpikeCatalystBoost"/>), abnormal dollar turnover with the price
/// moving against the shorts (+<see cref="VolumeSurgeCatalystBoost"/>), and a scheduled
/// earnings event inside the squeeze window
/// (+<see cref="EarningsProximityCatalystBoost"/>, via any registered
/// <see cref="IEarningsProximitySource"/>).</para>
///
/// <para>Factors a stock has no data for drop out and their weight is redistributed over
/// the rest. The universe is every stock reporting short interest at the latest
/// settlement date with a known shares-outstanding count and a physically credible
/// short-interest ratio (see <see cref="MaxCredibleShortInterestRatio"/>).</para>
/// </summary>
[Service]
public class ShortSqueezeScoreManager
{
    /// <summary>Each trend window pools two calendar weeks of daily short-volume rows.</summary>
    public const int TrendWindowDays = 14;

    /// <summary>
    /// Trailing calendar window over which fails-to-deliver pressure is measured,
    /// ending at the SEC feed's latest settlement date (the feed publishes with a
    /// multi-week lag, so "latest available" is the honest anchor).
    /// </summary>
    public const int FailsToDeliverWindowDays = 30;

    /// <summary>
    /// Calendar days of daily bars loaded per stock for the price factors — enough
    /// for the 60-trading-day baseline plus the recent week plus market holidays.
    /// </summary>
    public const int PriceHistoryCalendarDays = 100;

    /// <summary>
    /// Factor weights, deliberate and documented rather than fitted: the two classic
    /// crowdedness measures carry half the composite, the underwater proxy and the
    /// short-volume trend (the "pressure is mounting" readings) 15% each, and the two
    /// noisier corroborating signals 10% each.
    /// </summary>
    public const double ShortInterestWeight = 0.30;
    public const double DaysToCoverWeight = 0.20;
    public const double PriceAboveVwapWeight = 0.15;
    public const double ShortVolumeTrendWeight = 0.15;
    public const double ShortInterestChangeWeight = 0.10;
    public const double FailsToDeliverWeight = 0.10;

    /// <summary>Rank points added when the weekly return is a statistical outlier.</summary>
    public const double PriceSpikeCatalystBoost = 10;

    /// <summary>Rank points added for abnormal dollar turnover on a positive move.</summary>
    public const double VolumeSurgeCatalystBoost = 10;

    /// <summary>
    /// Rank points added when a scheduled earnings event is close — squeezes cluster
    /// around earnings announcements, so a crowded short base near one is at elevated
    /// risk. Fires only when a registered <see cref="IEarningsProximitySource"/>
    /// knows the stock's earnings date; no calendar data simply means no boost.
    /// </summary>
    public const double EarningsProximityCatalystBoost = 10;

    /// <summary>The earnings window opens this many weekdays before the event.</summary>
    public const int EarningsProximityLeadWeekdays = 5;

    /// <summary>The earnings window closes this many weekdays after the event.</summary>
    public const int EarningsProximityTrailWeekdays = 3;

    /// <summary>Ceiling on the total catalyst boost, mirroring the published models.</summary>
    public const double MaxCatalystBoost = 20;

    /// <summary>
    /// The listed-security kinds that can never be squeeze candidates: FINRA
    /// reports short interest on these tickers (exchange-traded notes,
    /// preferred series, warrants, rights), but the issuer's common-share
    /// record is the wrong denominator for them, so no honest ratio exists.
    /// Classified from the SEC 12(b) cover-page title (see
    /// <see cref="ListedSecurityType"/>); Unknown/Other/Units stay in — MLP
    /// common units are genuine operating equity, and exclusion requires
    /// positive evidence.
    /// </summary>
    private static readonly ListedSecurityType[] NonEquityListingTypes =
    [
        ListedSecurityType.PreferredShares,
        ListedSecurityType.DebtSecurities,
        ListedSecurityType.Warrants,
        ListedSecurityType.Rights,
    ];

    /// <summary>
    /// Highest short-interest-to-shares-outstanding ratio accepted as a real measurement. No
    /// genuine reading has ever approached this bound (the January 2021 GameStop peak, the most
    /// extreme on record, was ~1.4x); a ratio beyond it proves the inputs are inconsistent — a
    /// wrong shares-outstanding record (e.g. a listed note's issuer whose common stock is one
    /// share held by its parent), a cover-page filing artifact, or a short position and share
    /// count on different split bases — so the stock is dropped from the scored universe, the
    /// same treatment as an unknown share count, rather than ranked on a meaningless figure.
    /// </summary>
    public const decimal MaxCredibleShortInterestRatio = 2m;

    private readonly ShortInterestRepository _shortInterestRepository;
    private readonly DailyShortVolumeRepository _dailyShortVolumeRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly StockSplitRepository _stockSplitRepository;
    private readonly FailToDeliverRepository _failToDeliverRepository;
    private readonly DailyStockPriceRepository _dailyStockPriceRepository;
    private readonly IEnumerable<IEarningsProximitySource> _earningsProximitySources;

    public ShortSqueezeScoreManager(
        ShortInterestRepository shortInterestRepository,
        DailyShortVolumeRepository dailyShortVolumeRepository,
        CommonStockRepository commonStockRepository,
        StockSplitRepository stockSplitRepository,
        FailToDeliverRepository failToDeliverRepository,
        DailyStockPriceRepository dailyStockPriceRepository,
        IEnumerable<IEarningsProximitySource> earningsProximitySources
    )
    {
        _shortInterestRepository = shortInterestRepository;
        _dailyShortVolumeRepository = dailyShortVolumeRepository;
        _commonStockRepository = commonStockRepository;
        _stockSplitRepository = stockSplitRepository;
        _failToDeliverRepository = failToDeliverRepository;
        _dailyStockPriceRepository = dailyStockPriceRepository;
        _earningsProximitySources = earningsProximitySources;
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

        // Guard DaysToCover with a server-side range check so a single FINRA-feed artifact whose
        // stored value falls outside System.Decimal can't throw OverflowException while the batch
        // is materialized and take down every page that builds the squeeze universe (the screener,
        // the squeeze board). The comparison runs in Postgres numeric space, so an out-of-range
        // figure never reaches the System.Decimal reader; it is treated as missing and the factor
        // is recomputed from the short position and average daily volume below, exactly as when
        // FINRA omits days-to-cover.
        var shortInterests = await _shortInterestRepository
            .GetBySettlementDate(settlementDate)
            .Select(s => new
            {
                s.CommonStockId,
                s.CurrentShortPosition,
                s.PreviousShortPosition,
                s.AverageDailyVolume,
                DaysToCover = s.DaysToCover >= decimal.MinValue && s.DaysToCover <= decimal.MaxValue
                    ? s.DaysToCover
                    : null,
            })
            .ToListAsync(cancellationToken);

        var stockIds = shortInterests.Select(s => s.CommonStockId).Distinct().ToList();
        var stocks = await _commonStockRepository
            .GetAll()
            .Where(s =>
                stockIds.Contains(s.Id)
                && s.SharesOutStanding > 0
                && !NonEquityListingTypes.Contains(s.ListedSecurityType)
            )
            .Select(s => new
            {
                s.Id,
                s.Ticker,
                s.SharesOutStanding,
                s.MarketCapitalization,
            })
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var trends = await LoadShortVolumeTrends(stockIds, settlementDate, cancellationToken);

        // Load every scored stock's splits once so the short position — a share COUNT
        // observed as-of the settlement date — can be restated onto today's basis before
        // it is divided by the CURRENT shares outstanding. Without this a stock that split
        // after the settlement date reports a short-interest-percent-of-shares off by the
        // split factor (e.g. a 10:1 split makes the raw ratio 10× too small).
        var splitsByStock = (
            await _stockSplitRepository
                .GetAll()
                .Where(s => stockIds.Contains(s.CommonStockId))
                .ToListAsync(cancellationToken)
        )
            .GroupBy(s => s.CommonStockId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<StockSplit>)g.ToList());

        // The previous settlement date anchors the split basis of PreviousShortPosition.
        // FINRA publishes the whole market on one bi-monthly cycle, so the second-newest
        // distinct date is every stock's previous report. (A few hundred dates, cheap.)
        var settlementDates = await _shortInterestRepository
            .GetAllSettlementDates()
            .ToListAsync(cancellationToken);
        var previousSettlementDate = settlementDates
            .Where(d => d < settlementDate)
            .OrderByDescending(d => d)
            .FirstOrDefault();

        var scoredStockIds = stocks.Keys.ToList();
        var failsToDeliver = await LoadFailsToDeliver(scoredStockIds, cancellationToken);
        var priceFactors = await LoadPriceFactors(scoredStockIds, cancellationToken);
        var nearEarnings = await LoadStocksNearEarnings(scoredStockIds, cancellationToken);

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

            var stockSplits = splitsByStock.TryGetValue(stock.Id, out var splitList)
                ? splitList
                : [];
            var shortInterestPercentOfShares = ShortInterestPercentOfShares(
                shortInterest.CurrentShortPosition,
                stock.SharesOutStanding,
                settlementDate,
                stockSplits
            );
            if (shortInterestPercentOfShares > MaxCredibleShortInterestRatio)
            {
                // Physically impossible reading — the share count or split basis is wrong for
                // this listing, so no honest score exists for it (see the constant's doc).
                continue;
            }

            // Liquidity context for consumers that gate the board (market cap and an
            // approximate daily dollar turnover). The ADV is a share count as-of the
            // settlement date, so restate it onto today's basis before multiplying by
            // the market-cap-implied CURRENT share price.
            double? marketCap = stock.MarketCapitalization > 0 ? stock.MarketCapitalization : null;
            double? averageDailyDollarVolume = null;
            if (marketCap != null && shortInterest.AverageDailyVolume > 0)
            {
                var advOnTodaysBasis = SplitAdjustment.AdjustShareCount(
                    shortInterest.AverageDailyVolume.Value,
                    settlementDate,
                    stockSplits
                );
                averageDailyDollarVolume =
                    advOnTodaysBasis * (marketCap.Value / stock.SharesOutStanding);
            }

            var factors = priceFactors.TryGetValue(stock.Id, out var stockFactors)
                ? stockFactors
                : new ShortSqueezePriceFactors();

            scores.Add(
                new ShortSqueezeScore
                {
                    CommonStockId = stock.Id,
                    Ticker = stock.Ticker,
                    SettlementDate = settlementDate,
                    MarketCapitalization = marketCap,
                    AverageDailyDollarVolume = averageDailyDollarVolume,
                    ShortInterestPercentOfShares = shortInterestPercentOfShares,
                    DaysToCover = daysToCover,
                    // TryGetValue, not GetValueOrDefault: a stock with no volume data
                    // must carry a null trend (factor drops out), never a zero trend.
                    ShortVolumeShareTrend = trends.TryGetValue(stock.Id, out var trend)
                        ? trend
                        : null,
                    ShortInterestChangePercent = ShortInterestChangePercent(
                        shortInterest.CurrentShortPosition,
                        settlementDate,
                        shortInterest.PreviousShortPosition,
                        previousSettlementDate,
                        stockSplits
                    ),
                    FailsToDeliverPercentOfShares = FailsToDeliverPercentOfShares(
                        stock.Id,
                        stock.SharesOutStanding,
                        failsToDeliver,
                        stockSplits
                    ),
                    PriceAboveVwap = factors.PriceAboveVwap,
                    HasPriceSpikeCatalyst = factors.HasPriceSpikeCatalyst,
                    HasVolumeSurgeCatalyst = factors.HasVolumeSurgeCatalyst,
                    HasEarningsProximityCatalyst = nearEarnings.Contains(stock.Id),
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

    /// <summary>
    /// Loads each scored stock's daily fails-to-deliver quantities over the trailing
    /// window ending at the SEC feed's latest settlement date, each paired with its
    /// date so it can be split-restated per observation. Returns null when the feed
    /// holds no data at all — then the factor is unknowable and must drop out for
    /// everyone, which is different from a covered stock reporting no fails (a true
    /// zero).
    /// </summary>
    private async Task<Dictionary<Guid, List<(DateOnly Date, long Quantity)>>> LoadFailsToDeliver(
        List<Guid> stockIds,
        CancellationToken cancellationToken
    )
    {
        var latestDate = await _failToDeliverRepository
            .GetLatestDate()
            .FirstOrDefaultAsync(cancellationToken);
        if (latestDate == default)
        {
            return null;
        }

        var windowStart = latestDate.AddDays(-FailsToDeliverWindowDays);
        var rows = await _failToDeliverRepository
            .GetAll()
            .Where(f =>
                f.SettlementDate > windowStart
                && f.SettlementDate <= latestDate
                && stockIds.Contains(f.CommonStockId)
            )
            .Select(f => new
            {
                f.CommonStockId,
                f.SettlementDate,
                f.Quantity,
            })
            .ToListAsync(cancellationToken);

        var quantities = new Dictionary<Guid, List<(DateOnly Date, long Quantity)>>();
        foreach (var row in rows)
        {
            if (!quantities.TryGetValue(row.CommonStockId, out var list))
            {
                list = [];
                quantities[row.CommonStockId] = list;
            }

            list.Add((row.SettlementDate, row.Quantity));
        }

        return quantities;
    }

    /// <summary>
    /// Unions the stocks every registered <see cref="IEarningsProximitySource"/>
    /// reports as inside the earnings window. With no sources registered (the
    /// open-source default) the set is empty and the catalyst never fires.
    /// </summary>
    private async Task<HashSet<Guid>> LoadStocksNearEarnings(
        List<Guid> stockIds,
        CancellationToken cancellationToken
    )
    {
        var nearEarnings = new HashSet<Guid>();
        foreach (var source in _earningsProximitySources)
        {
            nearEarnings.UnionWith(await source.GetStocksNearEarnings(stockIds, cancellationToken));
        }

        return nearEarnings;
    }

    /// <summary>
    /// Loads the trailing daily bars for the scored universe in one query and computes
    /// each stock's price factors. The universe's latest price date anchors the
    /// staleness check inside the calculator, so a halted or delisted series produces
    /// no price factors instead of factors about the past.
    /// </summary>
    private async Task<Dictionary<Guid, ShortSqueezePriceFactors>> LoadPriceFactors(
        List<Guid> stockIds,
        CancellationToken cancellationToken
    )
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = today.AddDays(-PriceHistoryCalendarDays);
        var bars = await _dailyStockPriceRepository
            .GetByStocks(stockIds, cutoff, today)
            .Select(p => new
            {
                p.CommonStockId,
                p.Date,
                p.AdjustedClose,
                p.Close,
                p.Volume,
            })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, ShortSqueezePriceFactors>();
        if (bars.Count == 0)
        {
            return result;
        }

        var universeLatestDate = bars.Max(b => b.Date);
        foreach (var group in bars.GroupBy(b => b.CommonStockId))
        {
            var series = group
                .OrderBy(b => b.Date)
                .Select(b => new ShortSqueezeDailyBar(b.Date, b.AdjustedClose, b.Close, b.Volume))
                .ToList();
            result[group.Key] = ShortSqueezePriceFactorCalculator.Compute(
                series,
                universeLatestDate
            );
        }

        return result;
    }

    /// <summary>
    /// Short interest as a fraction of shares outstanding, with the short position
    /// restated onto today's split basis first. The short position is a share COUNT
    /// as-of <paramref name="settlementDate"/>; the shares-outstanding denominator is
    /// the CURRENT count, so the two must sit on the same basis before dividing. When
    /// the stock has no splits after the settlement date the factor is 1 and the ratio
    /// is unchanged.
    /// </summary>
    private static decimal ShortInterestPercentOfShares(
        long currentShortPosition,
        long sharesOutstanding,
        DateOnly settlementDate,
        IReadOnlyList<StockSplit> splits
    )
    {
        var factor = SplitAdjustment.ShareCountFactor(settlementDate, splits);
        return currentShortPosition * factor / sharesOutstanding;
    }

    /// <summary>
    /// Change in the short position versus the previous FINRA report as a fraction of
    /// the previous position, with BOTH positions restated onto today's split basis —
    /// a split between the two reports would otherwise masquerade as a position change
    /// of the split factor. Null when the previous position or its date is unknown.
    /// </summary>
    private static decimal? ShortInterestChangePercent(
        long currentPosition,
        DateOnly settlementDate,
        long previousPosition,
        DateOnly previousSettlementDate,
        IReadOnlyList<StockSplit> splits
    )
    {
        if (previousPosition <= 0 || previousSettlementDate == default)
        {
            return null;
        }

        var currentOnTodaysBasis = SplitAdjustment.AdjustShareCount(
            currentPosition,
            settlementDate,
            splits
        );
        var previousOnTodaysBasis = SplitAdjustment.AdjustShareCount(
            previousPosition,
            previousSettlementDate,
            splits
        );
        if (previousOnTodaysBasis <= 0)
        {
            return null;
        }

        return (currentOnTodaysBasis - previousOnTodaysBasis) / (decimal)previousOnTodaysBasis;
    }

    /// <summary>
    /// The stock's worst trailing single-day fails-to-deliver quantity — each daily
    /// observation restated onto today's split basis as-of its own settlement date —
    /// as a fraction of shares outstanding. A stock the live feed reports no fails
    /// for scores a true zero; when the feed has no data at all
    /// (<paramref name="failsToDeliver"/> is null) the factor is null and drops out
    /// of the composite.
    /// </summary>
    private static decimal? FailsToDeliverPercentOfShares(
        Guid stockId,
        long sharesOutstanding,
        Dictionary<Guid, List<(DateOnly Date, long Quantity)>> failsToDeliver,
        IReadOnlyList<StockSplit> splits
    )
    {
        if (failsToDeliver == null)
        {
            return null;
        }

        if (!failsToDeliver.TryGetValue(stockId, out var quantities))
        {
            return 0m;
        }

        var worst = 0L;
        foreach (var (date, quantity) in quantities)
        {
            var onTodaysBasis = SplitAdjustment.AdjustShareCount(quantity, date, splits);
            worst = Math.Max(worst, onTodaysBasis);
        }

        return (decimal)worst / sharesOutstanding;
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
        var changePercentiles = Percentiles(
            scores
                .Where(s => s.ShortInterestChangePercent != null)
                .Select(s => (s.CommonStockId, s.ShortInterestChangePercent.Value))
        );
        var failsToDeliverPercentiles = Percentiles(
            scores
                .Where(s => s.FailsToDeliverPercentOfShares != null)
                .Select(s => (s.CommonStockId, s.FailsToDeliverPercentOfShares.Value))
        );
        var priceAboveVwapPercentiles = Percentiles(
            scores
                .Where(s => s.PriceAboveVwap != null)
                .Select(s => (s.CommonStockId, s.PriceAboveVwap.Value))
        );

        foreach (var score in scores)
        {
            score.ShortInterestPercentile = shortInterestPercentiles[score.CommonStockId];
            score.DaysToCoverPercentile = Lookup(daysToCoverPercentiles, score.CommonStockId);
            score.ShortVolumeTrendPercentile = Lookup(trendPercentiles, score.CommonStockId);
            score.ShortInterestChangePercentile = Lookup(changePercentiles, score.CommonStockId);
            score.FailsToDeliverPercentile = Lookup(failsToDeliverPercentiles, score.CommonStockId);
            score.PriceAboveVwapPercentile = Lookup(priceAboveVwapPercentiles, score.CommonStockId);

            // Weighted mean over the factors this stock has data for: a missing
            // factor's weight is redistributed by dividing by the sum of the
            // weights actually present.
            var total = ShortInterestWeight * score.ShortInterestPercentile;
            var weight = ShortInterestWeight;
            if (score.DaysToCoverPercentile != null)
            {
                total += DaysToCoverWeight * score.DaysToCoverPercentile.Value;
                weight += DaysToCoverWeight;
            }

            if (score.ShortVolumeTrendPercentile != null)
            {
                total += ShortVolumeTrendWeight * score.ShortVolumeTrendPercentile.Value;
                weight += ShortVolumeTrendWeight;
            }

            if (score.ShortInterestChangePercentile != null)
            {
                total += ShortInterestChangeWeight * score.ShortInterestChangePercentile.Value;
                weight += ShortInterestChangeWeight;
            }

            if (score.FailsToDeliverPercentile != null)
            {
                total += FailsToDeliverWeight * score.FailsToDeliverPercentile.Value;
                weight += FailsToDeliverWeight;
            }

            if (score.PriceAboveVwapPercentile != null)
            {
                total += PriceAboveVwapWeight * score.PriceAboveVwapPercentile.Value;
                weight += PriceAboveVwapWeight;
            }

            score.BaseScore = Math.Round(total / weight, 2);

            var boost = 0d;
            if (score.HasPriceSpikeCatalyst)
            {
                boost += PriceSpikeCatalystBoost;
            }

            if (score.HasVolumeSurgeCatalyst)
            {
                boost += VolumeSurgeCatalystBoost;
            }

            if (score.HasEarningsProximityCatalyst)
            {
                boost += EarningsProximityCatalystBoost;
            }

            score.CatalystBoost = Math.Min(boost, MaxCatalystBoost);
            score.Score = Math.Round(Math.Min(100, score.BaseScore + score.CatalystBoost), 2);
        }
    }

    private static double? Lookup(Dictionary<Guid, double> percentiles, Guid stockId) =>
        percentiles.TryGetValue(stockId, out var value) ? value : null;

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
