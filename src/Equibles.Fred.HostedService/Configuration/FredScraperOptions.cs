using Equibles.Worker;

namespace Equibles.Fred.HostedService.Configuration;

public class FredScraperOptions : ScraperOptions
{
    // The earliest date FRED history is filled back to (FredScraper:HistoryStart). Unset, each series is filled to its
    // own first observation; Worker:MinSyncDate bounds only the incremental import, since a series is a few thousand rows.
    public DateTime? HistoryStart { get; set; }
}
