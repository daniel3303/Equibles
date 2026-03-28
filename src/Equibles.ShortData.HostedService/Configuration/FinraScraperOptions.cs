namespace Equibles.ShortData.HostedService.Configuration;

public class FinraScraperOptions {
    public int SleepIntervalHours { get; set; } = 24;
    public List<string> TickersToSync { get; set; } = [];
}
