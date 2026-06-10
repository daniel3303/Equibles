using Equibles.Worker;

namespace Equibles.Sec.FinancialFacts.HostedService.Configuration;

/// <summary>
/// Options for the dimensional-fact extraction sweep. The sweep is local
/// (database + CPU only — it reads already-captured envelopes, never EDGAR),
/// so the batch size bounds per-cycle memory, not a request budget.
/// </summary>
public class XbrlFactsExtractionOptions : ScraperOptions
{
    /// <summary>Documents processed per drain iteration.</summary>
    public int BatchSize { get; set; } = 100;
}
