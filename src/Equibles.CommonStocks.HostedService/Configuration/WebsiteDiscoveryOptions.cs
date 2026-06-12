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
}
