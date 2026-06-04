using MathNet.Numerics.Statistics;

namespace Equibles.Web.Extensions;

public static class StatisticsExtensions
{
    // Returns null when the (decimal) cast would throw OverflowException — i.e. NaN,
    // infinity, or a finite value outside decimal's representable range (~7.9e28).
    // Protects view-model casts: DescriptiveStatistics.StandardDeviation returns NaN
    // for a single-value sample, and variance/ratio math can produce huge finites.
    public static decimal? SafeRound(this double value, int digits)
    {
        if (
            !double.IsFinite(value)
            || value >= (double)decimal.MaxValue
            || value <= (double)decimal.MinValue
        )
            return null;
        return (decimal?)Math.Round(value, digits);
    }

    // Simple moving average aligned to the input: positions before the first full window
    // are null, subsequent positions are the rounded SMA value.
    public static List<decimal?> ComputeSma(this double[] values, int period, int digits)
    {
        var sma = values.MovingAverage(period);
        return sma.Select((v, i) => i < period - 1 ? (decimal?)null : v.SafeRound(digits)).ToList();
    }

    public static StatsSummary ComputeStats(this double[] values, int decimals)
    {
        // An empty sample has no defined statistics. DescriptiveStatistics.Mean and
        // Median() both return a finite 0.0 here, so SafeRound's non-finite guard never
        // fires for them (unlike Min/Max/StdDev, which come back NaN) — without this guard
        // a "no data" sample would surface a mean and median of 0. Return an all-null summary.
        if (values.Length == 0)
            return new StatsSummary(Mean: null, Median: null, Min: null, Max: null, StdDev: null);

        var stats = new DescriptiveStatistics(values);
        return new StatsSummary(
            Mean: stats.Mean.SafeRound(decimals),
            Median: values.Median().SafeRound(decimals),
            Min: stats.Minimum.SafeRound(decimals),
            Max: stats.Maximum.SafeRound(decimals),
            StdDev: stats.StandardDeviation.SafeRound(decimals)
        );
    }
}
