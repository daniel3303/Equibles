using System.Globalization;

namespace Equibles.Holdings.BusinessLogic.Models;

public sealed record HoldingsComparisonStatus(
    DateOnly ReportDate,
    DateOnly PreviousReportDate,
    IReadOnlySet<Guid> MissingPreviousFilers,
    IReadOnlySet<Guid> MissingCurrentFilers
)
{
    private const int NamedExclusions = 3;

    // Every missing filer's restated shares on the side where it holds the stock; the note
    // reports their counts, sums and largest names.
    public IReadOnlyList<HoldingsExcludedFiler> ExcludedFilers { get; init; } = [];

    // Both share totals over filers with an observed 13F in both quarters, summed from the same
    // rows as the exclusions. Only History computes it; null means no like-for-like change.
    public (long Current, long Previous)? LikeForLike { get; init; }

    // Filers that continue under other CIKs across this comparison. Comparable groups are
    // already present on both sides; the others stay among the missing filers.
    public IReadOnlyList<HoldingsSuccessionGroup> Successions { get; init; } = [];

    public bool ConsecutiveQuarters =>
        PreviousReportDate == HoldingsCorpusCoverage.PreviousQuarterEnd(ReportDate);

    // Some filer is always missing from one side of a widely held stock, so consecutive quarters
    // compare like-for-like instead of withholding the change.
    public bool ComparisonAvailable => ConsecutiveQuarters;

    // A predecessor is compared through its successor, and a group split across several
    // successors is compared only in the totals, since its prior position cannot be divided.
    public bool CanCompareHolder(Guid holderId) =>
        ConsecutiveQuarters
        && !MissingPreviousFilers.Contains(holderId)
        && !MissingCurrentFilers.Contains(holderId)
        && !Successions.Any(group =>
            group.Comparable
            && (
                group.Predecessors.ContainsKey(holderId)
                || (!group.SingleSuccessor && group.Contains(holderId))
            )
        );

    // The holder a filer's rows are compared under: the successor that alone continues it, or
    // the filer itself. Per-holder rankings group rows by this key before comparing them.
    public Guid ComparisonKey(Guid holderId) =>
        Successions
            .Where(group =>
                group.Comparable
                && group.SingleSuccessor
                && group.Predecessors.ContainsKey(holderId)
            )
            .Select(group => group.Successors.Keys.Single())
            .DefaultIfEmpty(holderId)
            .First();

    // The filers a successor alone continues, for its ranking row; null for any other holder.
    public string ContinuedFrom(Guid holderId) =>
        Successions
            .Where(group =>
                group.Comparable && group.SingleSuccessor && group.Successors.ContainsKey(holderId)
            )
            .Select(group =>
                group.Predecessors.Count == 1
                    ? group.Predecessors.Values.Single() ?? "Unknown filer"
                    : $"{Number(group.Predecessors.Count)} filers"
            )
            .FirstOrDefault();

    public long ExcludedCurrentShares => SharesOf(MissingPreviousFilers);

    public long ExcludedPreviousShares => SharesOf(MissingCurrentFilers);

    public IEnumerable<HoldingsExcludedFiler> LargestExcludedFilers =>
        ExcludedFilers
            .Where(filer => filer.Shares > 0)
            .OrderByDescending(filer => filer.Shares)
            .ThenBy(filer => filer.InstitutionalHolderId)
            .Take(NamedExclusions);

    // The sentence naming the filers compared across a CIK change, or null when none are. A
    // surface that shows every missing filer as new or sold out, rather than excluding them, uses it alone.
    public string SuccessionNote
    {
        get
        {
            if (!ConsecutiveQuarters)
                return null;
            var continued = Successions
                .Where(group => group.Comparable)
                .Select(Continued)
                .Order(StringComparer.Ordinal)
                .ToList();
            return continued.Count > 0
                ? $"Compared across CIK changes: {string.Join("; ", continued)}."
                : null;
        }
    }

    public string Note
    {
        get
        {
            if (!ConsecutiveQuarters)
                return "Comparison unavailable: the stored report dates are not consecutive quarters.";
            var successions = SuccessionNote;
            if (MissingPreviousFilers.Count == 0 && MissingCurrentFilers.Count == 0)
                return successions;

            var withShares = ExcludedFilers.Count > 0;
            var parts = new List<string>();
            if (MissingPreviousFilers.Count > 0)
                parts.Add(
                    $"{Holders(MissingPreviousFilers.Count)} with no observed {Date(PreviousReportDate)} 13F"
                        + (withShares ? $" ({Number(ExcludedCurrentShares)} shares)" : "")
                );
            if (MissingCurrentFilers.Count > 0)
                parts.Add(
                    $"{Holders(MissingCurrentFilers.Count)} with no observed {Date(ReportDate)} 13F"
                        + (
                            withShares
                                ? $" ({Number(ExcludedPreviousShares)} shares as of {Date(PreviousReportDate)})"
                                : ""
                        )
                );
            var largest = LargestExcludedFilers
                .Select(filer =>
                    $"{filer.Name ?? "Unknown filer"} {Number(filer.Shares)} ({Reason(filer.InstitutionalHolderId)})"
                )
                .ToList();
            return $"Excluded {string.Join(" and ", parts)}"
                + (largest.Count > 0 ? $"; largest: {string.Join(", ", largest)}" : "")
                + "."
                + (successions == null ? "" : $" {successions}");
        }
    }

    private string Reason(Guid holderId) =>
        Successions.Any(group => !group.Comparable && group.Contains(holderId))
            ? "CIK change, books not comparable"
            : $"no {Date(MissingPreviousFilers.Contains(holderId) ? PreviousReportDate : ReportDate)} 13F";

    private static string Continued(HoldingsSuccessionGroup group)
    {
        var from =
            group.Predecessors.Count == 1
                ? group.Predecessors.Values.Single() ?? "Unknown filer"
                : $"{Number(group.Predecessors.Count)} filers";
        var to =
            group.Successors.Count == 1
                ? group.Successors.Values.Single() ?? "Unknown filer"
                : $"{Number(group.Successors.Count)} filers, not ranked one by one";
        return $"{from} {(group.Predecessors.Count == 1 ? "continues" : "continue")} as {to}";
    }

    private long SharesOf(IReadOnlySet<Guid> holders) =>
        ExcludedFilers
            .Where(filer => holders.Contains(filer.InstitutionalHolderId))
            .Sum(filer => filer.Shares);

    private static string Holders(int count) =>
        count == 1 ? "1 holder" : $"{Number(count)} holders";

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
