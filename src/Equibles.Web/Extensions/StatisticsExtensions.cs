using MathNet.Numerics.Statistics;

namespace Equibles.Web.Extensions;

public static class StatisticsExtensions {
    // Returns null when the value is NaN or infinity — protects view-model casts from
    // OverflowException, since (decimal)NaN throws. DescriptiveStatistics.StandardDeviation
    // returns NaN for a single-value sample, which is the realistic trigger.
    public static decimal? SafeRound(this double value, int digits) {
        return double.IsFinite(value) ? (decimal?)Math.Round(value, digits) : null;
    }

    // Simple moving average aligned to the input: positions before the first full window
    // are null, subsequent positions are the rounded SMA value.
    public static List<decimal?> ComputeSma(this double[] values, int period, int digits) {
        var sma = values.MovingAverage(period);
        return sma.Select((v, i) => i < period - 1 ? (decimal?)null : (decimal?)Math.Round(v, digits)).ToList();
    }
}
