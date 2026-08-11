using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Equibles.Holdings.BusinessLogic;

/// <summary>
/// Defines the first complete 13F report quarter implied by the ingest floor. Filings before
/// that boundary are incidental and must not be ranked or used as a comparison baseline.
/// </summary>
[Service]
public class HoldingsCorpusCoverage
{
    public static readonly DateOnly DefaultMinSyncDate = new(2020, 1, 1);

    public static HoldingsCorpusCoverage Default { get; } = new(DefaultMinSyncDate);

    public HoldingsCorpusCoverage(IOptions<WorkerOptions> workerOptions)
        : this(
            workerOptions.Value.MinSyncDate is { } configured
                ? DateOnly.FromDateTime(configured)
                : DefaultMinSyncDate
        ) { }

    internal HoldingsCorpusCoverage(DateOnly minSyncDate)
    {
        MinSyncDate = minSyncDate;
        CoverageStartDate = FirstQuarterEndOnOrAfter(minSyncDate);
    }

    public DateOnly MinSyncDate { get; }

    public DateOnly CoverageStartDate { get; }

    public HoldingsCorpusStatus Evaluate(DateOnly reportDate, DateOnly? previousReportDate)
    {
        if (reportDate < CoverageStartDate)
        {
            return new HoldingsCorpusStatus(
                CoverageStartDate,
                IsWithinCoverage: false,
                ComparisonAvailable: false,
                $"Complete 13F coverage begins with {CoverageStartDate:yyyy-MM-dd}; "
                    + $"{reportDate:yyyy-MM-dd} is outside the complete corpus."
            );
        }

        if (previousReportDate is not { } previous || previous < CoverageStartDate)
        {
            var prior = previousReportDate is { } value
                ? $"the prior {value:yyyy-MM-dd} report date predates"
                : "there is no prior report date within";
            return new HoldingsCorpusStatus(
                CoverageStartDate,
                IsWithinCoverage: true,
                ComparisonAvailable: false,
                $"Quarter-over-quarter comparison is unavailable because {prior} complete "
                    + $"coverage, which begins {CoverageStartDate:yyyy-MM-dd}."
            );
        }

        return new HoldingsCorpusStatus(
            CoverageStartDate,
            IsWithinCoverage: true,
            ComparisonAvailable: true,
            ComparisonUnavailableReason: null
        );
    }

    public static DateOnly FirstQuarterEndOnOrAfter(DateOnly date)
    {
        var quarterEndMonth = ((date.Month - 1) / 3 + 1) * 3;
        return new DateOnly(
            date.Year,
            quarterEndMonth,
            DateTime.DaysInMonth(date.Year, quarterEndMonth)
        );
    }

    public static DateOnly PreviousQuarterEnd(DateOnly quarterEnd)
    {
        var quarterStartMonth = ((quarterEnd.Month - 1) / 3) * 3 + 1;
        return new DateOnly(quarterEnd.Year, quarterStartMonth, 1).AddDays(-1);
    }
}
