namespace Equibles.Yahoo.Repositories;

/// <summary>
/// Computes common technical indicators from price series.
/// All methods return lists matching the input length, null-padded at the start
/// where insufficient data exists for the lookback period.
/// </summary>
public static class TechnicalIndicatorService
{
    public static List<decimal?> ComputeSma(List<decimal> prices, int period)
    {
        var result = new List<decimal?>(prices.Count);
        for (var i = 0; i < prices.Count; i++)
        {
            if (i < period - 1)
            {
                result.Add(null);
                continue;
            }

            var sum = 0m;
            for (var j = i - period + 1; j <= i; j++)
            {
                sum += prices[j];
            }
            result.Add(Math.Round(sum / period, 4));
        }
        return result;
    }

    public static List<decimal?> ComputeEma(List<decimal> prices, int period)
    {
        var result = new List<decimal?>(prices.Count);
        var multiplier = 2m / (period + 1);

        for (var i = 0; i < prices.Count; i++)
        {
            if (i < period - 1)
            {
                result.Add(null);
                continue;
            }

            if (i == period - 1)
            {
                // Seed EMA with SMA for the first value
                var sum = 0m;
                for (var j = 0; j < period; j++)
                    sum += prices[j];
                result.Add(Math.Round(sum / period, 4));
                continue;
            }

            var prevEma = result[i - 1].Value;
            var ema = (prices[i] - prevEma) * multiplier + prevEma;
            result.Add(Math.Round(ema, 4));
        }
        return result;
    }

    public static List<decimal?> ComputeRsi(List<decimal> prices, int period = 14)
    {
        var result = new List<decimal?>(prices.Count);
        if (prices.Count <= period)
        {
            result.AddRange(Enumerable.Repeat<decimal?>(null, prices.Count));
            return result;
        }

        // First value is null (no change for index 0)
        result.Add(null);

        // Compute price changes
        var gains = new decimal[prices.Count];
        var losses = new decimal[prices.Count];
        for (var i = 1; i < prices.Count; i++)
        {
            var change = prices[i] - prices[i - 1];
            gains[i] = change > 0 ? change : 0;
            losses[i] = change < 0 ? -change : 0;
        }

        // First average gain/loss (simple average over first `period` changes)
        var avgGain = 0m;
        var avgLoss = 0m;
        for (var i = 1; i <= period; i++)
        {
            avgGain += gains[i];
            avgLoss += losses[i];
        }
        avgGain /= period;
        avgLoss /= period;

        // Pad nulls for the lookback period
        for (var i = 1; i < period; i++)
            result.Add(null);

        // avgLoss == 0 ⇒ RS is infinite ⇒ RSI is 100 by definition (a window
        // with no losses is the overbought extreme).
        static decimal RsiFrom(decimal avgGain, decimal avgLoss) =>
            avgLoss == 0 ? 100m : Math.Round(100m - 100m / (1m + avgGain / avgLoss), 2);

        result.Add(RsiFrom(avgGain, avgLoss));

        for (var i = period + 1; i < prices.Count; i++)
        {
            avgGain = (avgGain * (period - 1) + gains[i]) / period;
            avgLoss = (avgLoss * (period - 1) + losses[i]) / period;

            result.Add(RsiFrom(avgGain, avgLoss));
        }

        return result;
    }

    /// <summary>
    /// On-Balance Volume. Running cumulative sum seeded at 0: add the bar's volume when
    /// close > prev_close, subtract when close < prev_close, leave unchanged on equality.
    /// Bar 0 emits the seed (0) since there is no prior close to compare against.
    /// Returns a list aligned to input length.
    /// </summary>
    public static List<long> ComputeObv(List<decimal> closes, List<long> volumes)
    {
        if (closes.Count != volumes.Count)
            throw new ArgumentException("closes and volumes must have the same length");

        var count = closes.Count;
        var result = new List<long>(count);
        if (count == 0)
            return result;

        long obv = 0;
        result.Add(obv);
        for (var i = 1; i < count; i++)
        {
            if (closes[i] > closes[i - 1])
                obv += volumes[i];
            else if (closes[i] < closes[i - 1])
                obv -= volumes[i];
            result.Add(obv);
        }
        return result;
    }

    /// <summary>
    /// Average True Range (Wilder, J. Welles). TR = max(high − low, |high − prev_close|,
    /// |low − prev_close|); ATR is seeded with the simple average of the first
    /// <paramref name="period"/> TRs and then smoothed via Wilder's recursive average:
    /// <c>atr_i = (atr_{i-1} × (period − 1) + tr_i) / period</c>. Returns a list aligned
    /// to input length, null-padded while the window fills.
    /// </summary>
    public static List<decimal?> ComputeAtr(
        List<decimal> highs,
        List<decimal> lows,
        List<decimal> closes,
        int period = 14
    )
    {
        if (highs.Count != lows.Count || lows.Count != closes.Count)
            throw new ArgumentException("highs, lows, and closes must all have the same length");

        var count = closes.Count;
        var result = new List<decimal?>(count);
        if (count == 0)
            return result;

        // True Range per bar. Bar 0 has no previous close — TR_0 collapses to high − low,
        // which is the standard convention.
        var trueRanges = new decimal[count];
        trueRanges[0] = highs[0] - lows[0];
        for (var i = 1; i < count; i++)
        {
            var prevClose = closes[i - 1];
            var range = highs[i] - lows[i];
            var upGap = Math.Abs(highs[i] - prevClose);
            var downGap = Math.Abs(lows[i] - prevClose);
            trueRanges[i] = Math.Max(range, Math.Max(upGap, downGap));
        }

        // Need at least `period` TR values before emitting an ATR — short series stays null.
        if (count < period)
        {
            for (var i = 0; i < count; i++)
                result.Add(null);
            return result;
        }

        // First (period - 1) bars are warm-up; the seed lands at index (period - 1).
        for (var i = 0; i < period - 1; i++)
            result.Add(null);

        decimal seed = 0m;
        for (var i = 0; i < period; i++)
            seed += trueRanges[i];
        seed /= period;
        result.Add(Math.Round(seed, 4));

        // Wilder smoothing: combine the running ATR with the next TR.
        var atr = seed;
        for (var i = period; i < count; i++)
        {
            atr = (atr * (period - 1) + trueRanges[i]) / period;
            result.Add(Math.Round(atr, 4));
        }

        return result;
    }

    /// <summary>
    /// Stochastic Oscillator. %K = 100 × (close - lowestLow) / (highestHigh - lowestLow)
    /// over a <paramref name="kPeriod"/> lookback; %D is the simple moving average of %K
    /// over <paramref name="dPeriod"/> bars. Returns lists matching the input length,
    /// null-padded at the start while the lookback window fills.
    /// </summary>
    public static (List<decimal?> K, List<decimal?> D) ComputeStochastic(
        List<decimal> highs,
        List<decimal> lows,
        List<decimal> closes,
        int kPeriod = 14,
        int dPeriod = 3
    )
    {
        if (highs.Count != lows.Count || lows.Count != closes.Count)
            throw new ArgumentException("highs, lows, and closes must all have the same length");

        var count = closes.Count;
        var k = new List<decimal?>(count);
        for (var i = 0; i < count; i++)
        {
            if (i < kPeriod - 1)
            {
                k.Add(null);
                continue;
            }

            var highestHigh = decimal.MinValue;
            var lowestLow = decimal.MaxValue;
            for (var j = i - kPeriod + 1; j <= i; j++)
            {
                if (highs[j] > highestHigh)
                    highestHigh = highs[j];
                if (lows[j] < lowestLow)
                    lowestLow = lows[j];
            }

            var range = highestHigh - lowestLow;
            // Flat range = no momentum signal. Conventional convention is %K = 50 (the
            // neutral midpoint) rather than 0 / divide-by-zero / NaN.
            var kValue = range == 0 ? 50m : Math.Round(100m * (closes[i] - lowestLow) / range, 4);
            k.Add(kValue);
        }

        // %D = SMA of %K over dPeriod bars. Computed inline so the SMA can window over
        // a list-of-nullables without leaking the warm-up nulls into the average.
        var d = new List<decimal?>(count);
        for (var i = 0; i < count; i++)
        {
            if (i < kPeriod - 1 + dPeriod - 1)
            {
                d.Add(null);
                continue;
            }

            var sum = 0m;
            for (var j = i - dPeriod + 1; j <= i; j++)
                sum += k[j].Value;
            d.Add(Math.Round(sum / dPeriod, 4));
        }

        return (k, d);
    }

    public static (
        List<decimal?> Line,
        List<decimal?> Signal,
        List<decimal?> Histogram
    ) ComputeMacd(
        List<decimal> prices,
        int fastPeriod = 12,
        int slowPeriod = 26,
        int signalPeriod = 9
    )
    {
        var fastEma = ComputeEma(prices, fastPeriod);
        var slowEma = ComputeEma(prices, slowPeriod);

        // MACD line = Fast EMA - Slow EMA
        var macdLine = new List<decimal?>(prices.Count);
        var macdValues = new List<decimal>(); // non-null values for signal EMA
        for (var i = 0; i < prices.Count; i++)
        {
            if (fastEma[i] == null || slowEma[i] == null)
            {
                macdLine.Add(null);
            }
            else
            {
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

        for (var i = 0; i < prices.Count; i++)
        {
            if (macdLine[i] == null)
            {
                signal.Add(null);
                histogram.Add(null);
            }
            else
            {
                var sig = signalIdx < signalFromMacd.Count ? signalFromMacd[signalIdx] : null;
                signal.Add(sig);
                histogram.Add(sig != null ? Math.Round(macdLine[i].Value - sig.Value, 4) : null);
                signalIdx++;
            }
        }

        return (macdLine, signal, histogram);
    }
}
