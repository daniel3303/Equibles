namespace Equibles.ShortData.HostedService.Configuration;

public class FinraScraperOptions {
    public DateTime? MinScrapingDate { get; set; }
    public int SleepIntervalHours { get; set; } = 24;
    public List<string> TickersToSync { get; set; } = [];
}
