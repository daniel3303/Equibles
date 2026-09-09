namespace Equibles.Holdings.BusinessLogic.Models;

public sealed record HoldingsComparisonStatus(
    DateOnly ReportDate,
    DateOnly PreviousReportDate,
    IReadOnlySet<Guid> MissingPreviousFilers,
    IReadOnlySet<Guid> MissingCurrentFilers
)
{
    public bool ConsecutiveQuarters =>
        PreviousReportDate == HoldingsCorpusCoverage.PreviousQuarterEnd(ReportDate);
    public bool ComparisonAvailable =>
        ConsecutiveQuarters && MissingPreviousFilers.Count == 0 && MissingCurrentFilers.Count == 0;

    public bool CanCompareHolder(Guid holderId) =>
        ConsecutiveQuarters
        && !MissingPreviousFilers.Contains(holderId)
        && !MissingCurrentFilers.Contains(holderId);

    public string Note =>
        !ConsecutiveQuarters
            ? "Comparison unavailable: the stored report dates are not consecutive quarters."
        : ComparisonAvailable ? null
        : $"Comparison unavailable: {MissingPreviousFilers.Count} current holders have no observed prior-quarter 13F and {MissingCurrentFilers.Count} prior holders have no observed current-quarter 13F. Missing filings or filer identity changes are not treated as buying or selling.";
}
