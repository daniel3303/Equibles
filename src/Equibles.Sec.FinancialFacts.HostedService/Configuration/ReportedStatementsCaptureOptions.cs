using Equibles.Worker;

namespace Equibles.Sec.FinancialFacts.HostedService.Configuration;

/// <summary>
/// Options for the as-reported statement capture sweep. Each document costs one EDGAR request
/// for FilingSummary.xml plus one per statement table, all funnelled through the shared SEC rate
/// limiter.
/// </summary>
public class ReportedStatementsCaptureOptions : ScraperOptions
{
    /// <summary>Documents captured per drain iteration.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// How many statement R-files to fetch concurrently per document. Bounded by the shared SEC
    /// rate limiter regardless, so this only governs how large a share of that budget the backfill
    /// claims — higher drains faster but leaves less headroom for the time-sensitive SEC sweeps.
    /// </summary>
    public int MaxParallelFetches { get; set; } = 5;
}
