using Equibles.Worker;

namespace Equibles.Yahoo.HostedService.Configuration;

public class YahooPriceScraperOptions : ScraperOptions
{
    /// <summary>
    /// Minimum hours between enrichment sweeps (key statistics + company profile — 2 extra Yahoo
    /// calls per stock, the bulk of a cycle's traffic). Price cycles run every
    /// <see cref="ScraperOptions.SleepIntervalHours"/>; only cycles where this interval has
    /// elapsed since the last enrichment sweep carry the enrichment calls, so the sleep interval
    /// can be short (fresh daily closes) without multiplying the per-stock enrichment traffic.
    /// </summary>
    public int EnrichmentIntervalHours { get; set; } = 24;
}
