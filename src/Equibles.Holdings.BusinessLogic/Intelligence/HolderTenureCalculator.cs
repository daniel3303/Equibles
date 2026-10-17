using Equibles.Holdings.BusinessLogic.Intelligence.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// Consecutive quarters a filer has held a security, read from bitmasks where bit i is the i-th
/// calendar quarter since <see cref="FirstQuarter"/> (bit 63 is the sign bit of a signed 64-bit mask):
/// one mask of quarters it held the security, one of quarters it filed a 13F at all.
/// </summary>
public static class HolderTenureCalculator
{
    /// <summary>The first quarter end the holder-intelligence engine covers.</summary>
    public static readonly DateOnly FirstQuarter = new(2020, 3, 31);

    public const int MaximumQuarters = 64;

    /// <summary>
    /// The longest run of quarters with no 13F at all that a held security bridges, so a filer
    /// whose odd quarters are filed confidentially keeps its tenure.
    /// </summary>
    public const int MaximumBridgedQuarters = 1;

    /// <summary>The quarter's bit index, or null for a date that is not a covered quarter end.</summary>
    public static int? QuarterIndex(DateOnly reportDate)
    {
        if (!HoldingsCorpusCoverage.IsCalendarQuarterEnd(reportDate))
            return null;
        var index =
            (reportDate.Year - FirstQuarter.Year) * 4 + (reportDate.Month - FirstQuarter.Month) / 3;
        return index is >= 0 and < MaximumQuarters ? index : null;
    }

    /// <summary>
    /// The run of quarters ending at <paramref name="quarter"/> in which the security was held, or
    /// null when it was not held then. A gap of unfiled quarters no longer than
    /// <see cref="MaximumBridgedQuarters"/> between held quarters counts as held; a quarter the
    /// filer filed without the security ends the run, and reaching
    /// <paramref name="firstCoveredQuarter"/> (where the corpus starts) censors it.
    /// </summary>
    public static HolderTenure? Tenure(
        long held,
        long filed,
        int quarter,
        int firstCoveredQuarter = 0
    )
    {
        CheckQuarter(quarter);
        CheckQuarter(firstCoveredQuarter);
        if (quarter < firstCoveredQuarter)
            throw new ArgumentOutOfRangeException(nameof(quarter), quarter, null);
        if (!IsSet(held, quarter))
            return null;
        var quarters = 1;
        var index = quarter - 1;
        while (index >= firstCoveredQuarter)
        {
            if (IsSet(held, index))
            {
                quarters++;
                index--;
                continue;
            }
            var gap = 0;
            while (
                index - gap >= firstCoveredQuarter
                && !IsSet(filed, index - gap)
                && !IsSet(held, index - gap)
            )
                gap++;
            if (gap == 0 || gap > MaximumBridgedQuarters || index - gap < firstCoveredQuarter)
                return new HolderTenure(quarters, false);
            if (!IsSet(held, index - gap))
                return new HolderTenure(quarters, false);
            quarters += gap;
            index -= gap;
        }
        return new HolderTenure(quarters, true);
    }

    /// <summary>
    /// The value-weighted tenure of a book in quarters and the value share whose tenure is
    /// censored; both null when no position carries value.
    /// </summary>
    public static HolderTenureSummary Summarize(
        IEnumerable<(decimal Value, HolderTenure Tenure)> positions
    )
    {
        decimal total = 0,
            weighted = 0,
            censored = 0;
        foreach (var (value, tenure) in positions)
        {
            if (value <= 0)
                continue;
            total += value;
            weighted += value * tenure.Quarters;
            if (tenure.Censored)
                censored += value;
        }
        return total > 0
            ? new HolderTenureSummary(weighted / total, censored / total)
            : new HolderTenureSummary(null, null);
    }

    public static long Set(long mask, int quarter)
    {
        CheckQuarter(quarter);
        return mask | (1L << quarter);
    }

    private static bool IsSet(long mask, int quarter) => (mask & (1L << quarter)) != 0;

    // A shift wraps past bit 63, so an out-of-range quarter would silently read another bit.
    private static void CheckQuarter(int quarter)
    {
        if (quarter is < 0 or >= MaximumQuarters)
            throw new ArgumentOutOfRangeException(nameof(quarter), quarter, null);
    }
}
