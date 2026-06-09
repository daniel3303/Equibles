using Equibles.Worker;

namespace Equibles.CommonStocks.HostedService.Configuration;

public class Q4IncScraperOptions : ScraperOptions
{
    /// <summary>
    /// Maximum number of Q4 Inc stocks scraped per cycle. The scrape is
    /// network-bound and politely rate-limited, so a cycle works through a bounded
    /// batch and the rest are picked up next cycle.
    /// </summary>
    public int BatchSize { get; set; } = 50;
}
