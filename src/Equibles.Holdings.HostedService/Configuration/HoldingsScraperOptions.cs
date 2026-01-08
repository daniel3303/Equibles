namespace Equibles.Holdings.HostedService.Configuration;

public class HoldingsScraperOptions {
    public DateTime? MinScrapingDate { get; set; }
    public List<string> TickersToSync { get; set; } = [];
}
