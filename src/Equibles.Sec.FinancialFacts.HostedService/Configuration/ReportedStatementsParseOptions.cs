using Equibles.Worker;

namespace Equibles.Sec.FinancialFacts.HostedService.Configuration;

/// <summary>
/// Options for the as-reported statement parse sweep. The sweep is local (it reads already-captured
/// R-file bundles, never EDGAR), so the batch size bounds per-cycle memory, not a request budget.
/// </summary>
public class ReportedStatementsParseOptions : ScraperOptions
{
    /// <summary>Documents parsed per drain iteration.</summary>
    public int BatchSize { get; set; } = 50;
}
