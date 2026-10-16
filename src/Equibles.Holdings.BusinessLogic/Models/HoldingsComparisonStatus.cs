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

    // Every missing filer's shares on the side where it holds the stock. The like-for-like change
    // subtracts them from both totals, so a partial list misstates it.
    public IReadOnlyList<HoldingsExcludedFiler> ExcludedFilers { get; init; } = [];

    // Both share totals over filers with an observed 13F in both quarters, summed from the same
    // rows as the exclusions. Only History computes it; null means no like-for-like change.
    public (long Current, long Previous)? LikeForLike { get; init; }

    public bool ConsecutiveQuarters =>
        PreviousReportDate == HoldingsCorpusCoverage.PreviousQuarterEnd(ReportDate);

    // Some filer is always missing from one side of a widely held stock, so consecutive quarters
    // compare like-for-like instead of withholding the change.
    public bool ComparisonAvailable => ConsecutiveQuarters;

    public bool CanCompareHolder(Guid holderId) =>
        ConsecutiveQuarters
        && !MissingPreviousFilers.Contains(holderId)
        && !MissingCurrentFilers.Contains(holderId);

    public long ExcludedCurrentShares => SharesOf(MissingPreviousFilers);

    public long ExcludedPreviousShares => SharesOf(MissingCurrentFilers);

    public IEnumerable<HoldingsExcludedFiler> LargestExcludedFilers =>
        ExcludedFilers
            .Where(filer => filer.Shares > 0)
            .OrderByDescending(filer => filer.Shares)
            .ThenBy(filer => filer.InstitutionalHolderId)
            .Take(NamedExclusions);

    public string Note
    {
        get
        {
            if (!ConsecutiveQuarters)
                return "Comparison unavailable: the stored report dates are not consecutive quarters.";
            if (MissingPreviousFilers.Count == 0 && MissingCurrentFilers.Count == 0)
                return null;

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
                    $"{filer.Name ?? "Unknown filer"} {Number(filer.Shares)} (no {Date(MissingPreviousFilers.Contains(filer.InstitutionalHolderId) ? PreviousReportDate : ReportDate)} 13F)"
                )
                .ToList();
            return $"Excluded {string.Join(" and ", parts)}"
                + (largest.Count > 0 ? $"; largest: {string.Join(", ", largest)}" : "")
                + ".";
        }
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
