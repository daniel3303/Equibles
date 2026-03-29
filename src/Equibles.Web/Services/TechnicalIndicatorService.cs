namespace Equibles.Web.Services;

/// <summary>
/// Computes common technical indicators from price series.
/// All methods return lists matching the input length, null-padded at the start
/// where insufficient data exists for the lookback period.
/// </summary>
public static class TechnicalIndicatorService {
    public static List<decimal?> ComputeSma(List<decimal> prices, int period) {
        var result = new List<decimal?>(prices.Count);
        for (var i = 0; i < prices.Count; i++) {
            if (i < period - 1) {
                result.Add(null);
                continue;
            }

            var sum = 0m;
            for (var j = i - period + 1; j <= i; j++) {
                sum += prices[j];
            }
            result.Add(Math.Round(sum / period, 4));
        }
        return result;
    }

    public static List<decimal?> ComputeEma(List<decimal> prices, int period) {
        var result = new List<decimal?>(prices.Count);
        var multiplier = 2m / (period + 1);

        for (var i = 0; i < prices.Count; i++) {
            if (i < period - 1) {
                result.Add(null);
                continue;
            }

            if (i == period - 1) {
                // Seed EMA with SMA for the first value
                var sum = 0m;
                for (var j = 0; j < period; j++) sum += prices[j];
                result.Add(Math.Round(sum / period, 4));
                continue;
            }

            var prevEma = result[i - 1].Value;
            var ema = (prices[i] - prevEma) * multiplier + prevEma;
            result.Add(Math.Round(ema, 4));
        }
        return result;
    }

    public static List<decimal?> ComputeRsi(List<decimal> prices, int period = 14) {
        var result = new List<decimal?>(prices.Count);
        if (prices.Count <= period) {
            result.AddRange(Enumerable.Repeat<decimal?>(null, prices.Count));
            return result;
        }

        // First value is null (no change for index 0)
        result.Add(null);

        // Compute price changes
        var gains = new decimal[prices.Count];
        var losses = new decimal[prices.Count];
        for (var i = 1; i < prices.Count; i++) {
            var change = prices[i] - prices[i - 1];
            gains[i] = change > 0 ? change : 0;
            losses[i] = change < 0 ? -change : 0;
        }

        // First average gain/loss (simple average over first `period` changes)
        var avgGain = 0m;
        var avgLoss = 0m;
        for (var i = 1; i <= period; i++) {
            avgGain += gains[i];
            avgLoss += losses[i];
        }
        avgGain /= period;
        avgLoss /= period;

        // Pad nulls for the lookback period
        for (var i = 1; i < period; i++) result.Add(null);

        // First RSI value
        var rs = avgLoss == 0 ? 100m : avgGain / avgLoss;
        result.Add(Math.Round(100m - 100m / (1m + rs), 2));

        // Smoothed RSI for remaining values
        for (var i = period + 1; i < prices.Count; i++) {
            avgGain = (avgGain * (period - 1) + gains[i]) / period;
            avgLoss = (avgLoss * (period - 1) + losses[i]) / period;

            rs = avgLoss == 0 ? 100m : avgGain / avgLoss;
            result.Add(Math.Round(100m - 100m / (1m + rs), 2));
        }

        return result;
    }

    public static (List<decimal?> Line, List<decimal?> Signal, List<decimal?> Histogram) ComputeMacd(
        List<decimal> prices, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9
    ) {
        var fastEma = ComputeEma(prices, fastPeriod);
        var slowEma = ComputeEma(prices, slowPeriod);

        // MACD line = Fast EMA - Slow EMA
        var macdLine = new List<decimal?>(prices.Count);
        var macdValues = new List<decimal>(); // non-null values for signal EMA
        for (var i = 0; i < prices.Count; i++) {
            if (fastEma[i] == null || slowEma[i] == null) {
                macdLine.Add(null);
            } else {
                var val = Math.Round(fastEma[i].Value - slowEma[i].Value, 4);
                macdLine.Add(val);
                macdValues.Add(val);
            }
        }

        // Signal line = EMA of MACD line
        var signalFromMacd = ComputeEma(macdValues, signalPeriod);

        // Map signal values back to full-length list
        var signal = new List<decimal?>(prices.Count);
        var histogram = new List<decimal?>(prices.Count);
        var signalIdx = 0;

        for (var i = 0; i < prices.Count; i++) {
            if (macdLine[i] == null) {
                signal.Add(null);
                histogram.Add(null);
            } else {
                var sig = signalIdx < signalFromMacd.Count ? signalFromMacd[signalIdx] : null;
                signal.Add(sig);
                histogram.Add(sig != null ? Math.Round(macdLine[i].Value - sig.Value, 4) : null);
                signalIdx++;
            }
        }

        return (macdLine, signal, histogram);
    }
}
