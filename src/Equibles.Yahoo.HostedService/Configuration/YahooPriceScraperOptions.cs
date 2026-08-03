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

    /// <summary>
    /// How many days back a stored bar is still re-read from the feed. A bar is stored as soon as
    /// its date rolls over in UTC — four hours after the US close — but the feed can still revise
    /// its OHLC and volume overnight. Without a re-read the first partial figure is permanent (the
    /// importer is otherwise insert-only).
    /// The default covers the previous session across a weekend, which is all steady-state
    /// operation needs. Raise it temporarily to resettle a longer stretch of stored bars: the
    /// window only ever widens a fetch that was already going to happen, so a wider setting costs
    /// no extra upstream calls, only a larger response and more rows compared per stock.
    /// </summary>
    public int VolumeResettleWindowDays { get; set; } = 5;

    /// <summary>
    /// Maximum number of historically-invalid OHLC rows repaired after each ordinary price pass.
    /// The repair is deliberately bounded so old corruption cannot delay current prices without
    /// limit. Set to zero to disable it.
    /// </summary>
    public int OhlcRepairBatchSize { get; set; } = 100;
}
