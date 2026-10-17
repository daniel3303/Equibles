using Equibles.Holdings.BusinessLogic.Intelligence.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// Consecutive quarters a filer has held a security, read from bitmasks where bit i is the i-th
/// calendar quarter since <see cref="FirstQuarter"/>: one mask of quarters it held the security, one
/// of quarters it filed a 13F at all.
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
        if (reportDate.AddDays(1).Day != 1 || reportDate.Month % 3 != 0)
            return null;
        var index =
            (reportDate.Year - FirstQuarter.Year) * 4 + (reportDate.Month - FirstQuarter.Month) / 3;
        return index is >= 0 and < MaximumQuarters ? index : null;
    }

    /// <summary>
    /// The run of quarters ending at <paramref name="quarter"/> in which the security was held, or
    /// null when it was not held then. A gap of unfiled quarters no longer than
    /// <see cref="MaximumBridgedQuarters"/> between held quarters counts as held; a quarter the
    /// filer filed without the security ends the run.
    /// </summary>
    public static HolderTenure? Tenure(ulong held, ulong filed, int quarter)
    {
        if (!IsSet(held, quarter))
            return null;
        var quarters = 1;
        var index = quarter - 1;
        while (index >= 0)
        {
            if (IsSet(held, index))
            {
                quarters++;
                index--;
                continue;
            }
            var gap = 0;
            while (index - gap >= 0 && !IsSet(filed, index - gap) && !IsSet(held, index - gap))
                gap++;
            if (gap == 0 || gap > MaximumBridgedQuarters || index - gap < 0)
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

    public static ulong Set(ulong mask, int quarter) => mask | (1UL << quarter);

    private static bool IsSet(ulong mask, int quarter) => (mask & (1UL << quarter)) != 0;
}
