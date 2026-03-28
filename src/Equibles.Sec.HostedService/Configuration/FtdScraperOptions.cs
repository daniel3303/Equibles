namespace Equibles.Sec.HostedService.Configuration;

public class FtdScraperOptions {
    public int SleepIntervalHours { get; set; } = 24;
    public List<string> TickersToSync { get; set; } = [];
}
