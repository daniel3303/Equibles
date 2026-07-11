using Equibles.Finra.BusinessLogic.Models;

namespace Equibles.Finra.BusinessLogic;

/// <summary>
/// Computes the price-derived squeeze factors from a stock's recent daily bars:
/// the shorts-underwater proxy (price versus trailing VWAP) and the two catalyst
/// flags (price spike, volume surge). Pure math over an already-loaded series so
/// the thresholds are unit-testable without a database.
/// </summary>
public static class ShortSqueezePriceFactorCalculator
{
    /// <summary>Bars in the recent window the catalysts measure (one trading week).</summary>
    public const int RecentWindowBars = 5;

    /// <summary>Bars in the baseline window the recent window is compared against.</summary>
    public const int BaselineWindowBars = 60;

    /// <summary>
    /// Minimum baseline bars required before the catalysts are evaluated — with
    /// fewer, the volatility and turnover baselines are too noisy to flag against.
    /// </summary>
    public const int MinBaselineBars = 30;

    /// <summary>Minimum bars required for the trailing VWAP to be meaningful.</summary>
    public const int MinVwapBars = 20;

    /// <summary>
    /// A series whose last bar is older than this many calendar days before the
    /// universe's latest price date is stale (halted or delisted) — price factors
    /// computed from it would describe the past, so none are produced.
    /// </summary>
    public const int MaxStaleCalendarDays = 10;

    /// <summary>
    /// The price-spike threshold: the recent-window return must be at least this
    /// many standard deviations of the same-length baseline return distribution
    /// (daily sigma scaled by the square root of the window length).
    /// </summary>
    public const double PriceSpikeSigmaMultiple = 2;

    /// <summary>
    /// The volume-surge threshold: recent average dollar turnover must be at
    /// least this multiple of the baseline average.
    /// </summary>
    public const double VolumeSurgeMultiple = 2;

    /// <summary>
    /// Computes the factors from <paramref name="bars"/> (ordered by date
    /// ascending, most recent last). <paramref name="universeLatestDate"/> is the
    /// most recent price date across the whole scored universe, used to reject
    /// stale series. Returns an empty result (null proxy, no catalysts) rather
    /// than null so callers never branch.
    /// </summary>
    public static ShortSqueezePriceFactors Compute(
        IReadOnlyList<ShortSqueezeDailyBar> bars,
        DateOnly universeLatestDate
    )
    {
        var factors = new ShortSqueezePriceFactors();
        if (bars.Count == 0)
        {
            return factors;
        }

        var last = bars[^1];
        if (last.Date < universeLatestDate.AddDays(-MaxStaleCalendarDays))
        {
            return factors;
        }

        factors.PriceAboveVwap = PriceAboveVwap(bars, last);

        // Catalysts compare the last RecentWindowBars against the up-to
        // BaselineWindowBars immediately before them.
        var baselineCount = Math.Min(bars.Count - RecentWindowBars, BaselineWindowBars);
        if (baselineCount < MinBaselineBars)
        {
            return factors;
        }

        var recentStart = bars.Count - RecentWindowBars;
        var baselineStart = recentStart - baselineCount;

        var entryClose = bars[recentStart - 1].AdjustedClose;
        if (entryClose <= 0)
        {
            return factors;
        }

        var recentReturn = (double)(last.AdjustedClose / entryClose - 1);
        if (recentReturn <= 0)
        {
            // Both catalysts require price moving AGAINST the shorts; a volume or
            // volatility burst on the way down is covering, not a squeeze trigger.
            return factors;
        }

        var dailySigma = BaselineDailyReturnSigma(bars, baselineStart, recentStart);
        factors.HasPriceSpikeCatalyst =
            dailySigma > 0
            && recentReturn >= PriceSpikeSigmaMultiple * dailySigma * Math.Sqrt(RecentWindowBars);

        var baselineTurnover = AverageDollarTurnover(bars, baselineStart, recentStart);
        var recentTurnover = AverageDollarTurnover(bars, recentStart, bars.Count);
        factors.HasVolumeSurgeCatalyst =
            baselineTurnover > 0 && recentTurnover >= VolumeSurgeMultiple * baselineTurnover;

        return factors;
    }

    /// <summary>
    /// The latest close versus the volume-weighted average adjusted close over the
    /// trailing window (up to baseline + recent bars). Falls back to the simple
    /// mean when the window traded no volume at all.
    /// </summary>
    private static decimal? PriceAboveVwap(
        IReadOnlyList<ShortSqueezeDailyBar> bars,
        ShortSqueezeDailyBar last
    )
    {
        var windowStart = Math.Max(0, bars.Count - (BaselineWindowBars + RecentWindowBars));
        var windowCount = bars.Count - windowStart;
        if (windowCount < MinVwapBars)
        {
            return null;
        }

        decimal priceVolumeSum = 0;
        decimal volumeSum = 0;
        decimal closeSum = 0;
        for (var i = windowStart; i < bars.Count; i++)
        {
            priceVolumeSum += bars[i].AdjustedClose * bars[i].Volume;
            volumeSum += bars[i].Volume;
            closeSum += bars[i].AdjustedClose;
        }

        var vwap = volumeSum > 0 ? priceVolumeSum / volumeSum : closeSum / windowCount;
        return vwap > 0 ? last.AdjustedClose / vwap - 1 : null;
    }

    /// <summary>
    /// Sample standard deviation of the day-over-day returns inside the baseline
    /// window. Zero when any close in the window is non-positive (a corrupt bar
    /// makes the distribution meaningless, which disables the spike catalyst).
    /// </summary>
    private static double BaselineDailyReturnSigma(
        IReadOnlyList<ShortSqueezeDailyBar> bars,
        int baselineStart,
        int recentStart
    )
    {
        var returns = new List<double>(recentStart - baselineStart);
        for (var i = baselineStart + 1; i < recentStart; i++)
        {
            var previous = bars[i - 1].AdjustedClose;
            if (previous <= 0 || bars[i].AdjustedClose <= 0)
            {
                return 0;
            }

            returns.Add((double)(bars[i].AdjustedClose / previous - 1));
        }

        if (returns.Count < 2)
        {
            return 0;
        }

        var mean = returns.Average();
        var sumOfSquares = returns.Sum(r => (r - mean) * (r - mean));
        return Math.Sqrt(sumOfSquares / (returns.Count - 1));
    }

    private static double AverageDollarTurnover(
        IReadOnlyList<ShortSqueezeDailyBar> bars,
        int start,
        int end
    )
    {
        double sum = 0;
        for (var i = start; i < end; i++)
        {
            sum += (double)bars[i].Close * bars[i].Volume;
        }

        return sum / (end - start);
    }
}
