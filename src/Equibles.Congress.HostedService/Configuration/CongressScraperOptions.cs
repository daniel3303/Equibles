namespace Equibles.Congress.HostedService.Configuration;

public class CongressScraperOptions {
    public DateTime? MinScrapingDate { get; set; }
    public List<string> TickersToSync { get; set; } = [];
}
