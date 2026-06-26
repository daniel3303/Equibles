using Equibles.Worker;

namespace Equibles.Sec.FinancialFacts.HostedService.Configuration;

/// <summary>
/// Options for the as-reported statement capture sweep. Each document costs one EDGAR request
/// for FilingSummary.xml plus one per statement table, so the batch size is a per-cycle request
/// budget against the shared SEC rate limit — modest by default.
/// </summary>
public class ReportedStatementsCaptureOptions : ScraperOptions
{
    /// <summary>Documents captured per drain iteration.</summary>
    public int BatchSize { get; set; } = 25;
}
