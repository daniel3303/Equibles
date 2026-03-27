namespace Equibles.Fred.HostedService.Configuration;

public class FredScraperOptions {
    public DateTime? MinScrapingDate { get; set; }
    public int SleepIntervalHours { get; set; } = 24;
}
