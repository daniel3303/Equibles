namespace Equibles.Yahoo.Repositories;

/// <summary>
/// Computes common technical indicators from price series.
/// All methods return lists matching the input length, null-padded at the start
/// where insufficient data exists for the lookback period.
/// </summary>
public static class TechnicalIndicatorService
{
    // OHLC-derived indicators (SMA, EMA, ATR, Stochastic, MACD) round to this precision.
    // RSI deliberately uses 2 decimals — it's a conventional 0–100 percentage.
    private const int RoundingDigits = 4;

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

            var sum = SumWindow(prices, i - period + 1, period);
            result.Add(Math.Round(sum / period, RoundingDigits));
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
                var sum = SumWindow(prices, 0, period);
                result.Add(Math.Round(sum / period, RoundingDigits));
                continue;
            }

            var prevEma = result[i - 1].Value;
            var ema = (prices[i] - prevEma) * multiplier + prevEma;
            result.Add(Math.Round(ema, RoundingDigits));
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
        result.Add(Math.Round(seed, RoundingDigits));

        // Wilder smoothing: combine the running ATR with the next TR.
        var atr = seed;
        for (var i = period; i < count; i++)
        {
            atr = (atr * (period - 1) + trueRanges[i]) / period;
            result.Add(Math.Round(atr, RoundingDigits));
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
            var kValue =
                range == 0
                    ? 50m
                    : Math.Round(100m * (closes[i] - lowestLow) / range, RoundingDigits);
            k.Add(kValue);
        }

        // %D = SMA of %K over dPeriod bars. Computed inline so the SMA can window over
        // a list-of-nullables without leaking the warm-up nulls into the average.
        var d = new List<decimal?>(count);
        for (var i = 0; i < count; i++)
        {
            // Computed in long: kPeriod + dPeriod can exceed int.MaxValue, and an int
            // overflow here would wrap the threshold negative so the guard never fires
            // and the inner loop indexes %K out of range.
            if (i < (long)kPeriod - 1 + dPeriod - 1)
            {
                d.Add(null);
                continue;
            }

            var sum = 0m;
            for (var j = i - dPeriod + 1; j <= i; j++)
                sum += k[j].Value;
            d.Add(Math.Round(sum / dPeriod, RoundingDigits));
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
                var val = Math.Round(fastEma[i].Value - slowEma[i].Value, RoundingDigits);
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
                histogram.Add(
                    sig != null ? Math.Round(macdLine[i].Value - sig.Value, RoundingDigits) : null
                );
                signalIdx++;
            }
        }

        return (macdLine, signal, histogram);
    }

    /// <summary>
    /// Detects the most recent moving-average crossover between a shorter average
    /// (e.g. 50-day) and a longer one (e.g. 200-day) within the last
    /// <paramref name="lookback"/> bars. A golden cross is the bar where the shorter
    /// average rises from at-or-below to strictly above the longer average; a death
    /// cross is the mirror. Both series must be aligned to the same bars (as produced
    /// by <see cref="ComputeSma"/>); nulls from the warm-up period are skipped.
    /// Returns <see cref="MovingAverageCrossSignal.None"/> when no crossover occurred
    /// in the window or there is insufficient data to compare adjacent bars.
    /// </summary>
    public static MovingAverageCrossSignal DetectMaCross(
        List<decimal?> shortMa,
        List<decimal?> longMa,
        int lookback = 5
    )
    {
        if (shortMa.Count != longMa.Count)
            throw new ArgumentException("shortMa and longMa must have the same length");

        // Walk adjacent bar-pairs from newest backwards; the first crossover found is
        // the most recent one. Each pair needs all four values present to compare.
        var lastIndex = shortMa.Count - 1;
        var oldestPair = Math.Max(1, lastIndex - lookback + 1);
        for (var i = lastIndex; i >= oldestPair; i--)
        {
            var currShort = shortMa[i];
            var currLong = longMa[i];
            var prevShort = shortMa[i - 1];
            var prevLong = longMa[i - 1];
            if (currShort == null || currLong == null || prevShort == null || prevLong == null)
                continue;

            if (prevShort <= prevLong && currShort > currLong)
                return MovingAverageCrossSignal.GoldenCross;
            if (prevShort >= prevLong && currShort < currLong)
                return MovingAverageCrossSignal.DeathCross;
        }

        return MovingAverageCrossSignal.None;
    }

    /// <summary>
    /// Counts the run of consecutive most-recent closes that each moved the same way
    /// versus the prior close. Returns the streak length and its direction; an
    /// unchanged close ends the run, and fewer than two prices yields a zero-length
    /// <see cref="PriceStreakDirection.None"/> streak.
    /// </summary>
    public static (int Days, PriceStreakDirection Direction) CountConsecutiveStreak(
        List<decimal> closes
    )
    {
        if (closes.Count < 2)
            return (0, PriceStreakDirection.None);

        var lastIndex = closes.Count - 1;
        var lastMove = closes[lastIndex] - closes[lastIndex - 1];
        if (lastMove == 0)
            return (0, PriceStreakDirection.None);

        var direction = lastMove > 0 ? PriceStreakDirection.Up : PriceStreakDirection.Down;
        var days = 0;
        for (var i = lastIndex; i >= 1; i--)
        {
            var move = closes[i] - closes[i - 1];
            var moveUp = move > 0;
            var moveDown = move < 0;
            var matchesUp = direction == PriceStreakDirection.Up && moveUp;
            var matchesDown = direction == PriceStreakDirection.Down && moveDown;
            if (!matchesUp && !matchesDown)
                break;
            days++;
        }

        return (days, direction);
    }

    /// <summary>
    /// Bollinger Bands. The middle band is the <paramref name="period"/>-bar simple moving
    /// average of close; the upper and lower bands sit <paramref name="stdDev"/> standard
    /// deviations above and below it. The population standard deviation (denominator =
    /// <paramref name="period"/>) is used, per John Bollinger's original definition.
    /// Returns three lists aligned to input length, null-padded at the start while the
    /// lookback window fills.
    /// </summary>
    public static (
        List<decimal?> Middle,
        List<decimal?> Upper,
        List<decimal?> Lower
    ) ComputeBollingerBands(List<decimal> prices, int period = 20, decimal stdDev = 2m)
    {
        var count = prices.Count;
        var middle = new List<decimal?>(count);
        var upper = new List<decimal?>(count);
        var lower = new List<decimal?>(count);

        for (var i = 0; i < count; i++)
        {
            if (i < period - 1)
            {
                middle.Add(null);
                upper.Add(null);
                lower.Add(null);
                continue;
            }

            var mean = SumWindow(prices, i - period + 1, period) / period;

            var sumSquares = 0m;
            for (var j = i - period + 1; j <= i; j++)
            {
                var diff = prices[j] - mean;
                sumSquares += diff * diff;
            }
            // Population standard deviation (denominator = period) — the Bollinger convention.
            var deviation = (decimal)Math.Sqrt((double)(sumSquares / period));
            var offset = stdDev * deviation;

            middle.Add(Math.Round(mean, RoundingDigits));
            upper.Add(Math.Round(mean + offset, RoundingDigits));
            lower.Add(Math.Round(mean - offset, RoundingDigits));
        }

        return (middle, upper, lower);
    }

    // Sum of `count` consecutive values starting at `start`, summed in ascending index
    // order so the decimal result is independent of how callers compute their windows.
    private static decimal SumWindow(List<decimal> values, int start, int count)
    {
        var sum = 0m;
        for (var i = start; i < start + count; i++)
            sum += values[i];
        return sum;
    }
}
