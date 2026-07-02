using Equibles.Worker;

namespace Equibles.CommonStocks.HostedService.Configuration;

public class WebsiteDiscoveryOptions : ScraperOptions
{
    /// <summary>
    /// Maximum number of stocks attempted per cycle. Candidate validation is
    /// network-bound, so a cycle works through a bounded batch and the remaining
    /// stocks are picked up on the next cycle.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Days to wait before re-attempting a stock whose last discovery attempt found
    /// no website in any source. Definitive misses are stamped on the stock, so
    /// persistent misses back off for this window; transient source errors are not
    /// stamped and retry on the next cycle.
    /// </summary>
    public int CheckCooldownDays { get; set; } = 30;

    /// <summary>
    /// How many candidate reachability probes to run concurrently within a cycle. Each probe is a
    /// page fetch that can take up to the render timeout for a dead/slow host, so a
    /// serial loop made a batch with many dead candidates take many minutes. Bounded to keep within
    /// the stealth sidecar's own concurrency cap (defaults align with StealthFetch:MaxConcurrency).
    /// </summary>
    public int ProbeConcurrency { get; set; } = 6;
}
