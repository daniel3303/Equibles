using Equibles.Worker;

namespace Equibles.EquityMarkets.HostedService.Configuration;

public class EquityMarketsScraperOptions : ScraperOptions
{
    public EquityMarketsScraperOptions() => SleepIntervalHours = 6;

    /// <summary>How often a market's directory is re-captured when no refresh was requested.</summary>
    public int DirectoryRefreshIntervalHours { get; set; } = 24;

    /// <summary>How often the directory worker checks registrations for work.</summary>
    public int DirectoryControlIntervalMinutes { get; set; } = 1;

    /// <summary>How long a market waits after a pass that captured nothing before it is tried again.</summary>
    public int DirectoryRetryIntervalMinutes { get; set; } = 15;
}
