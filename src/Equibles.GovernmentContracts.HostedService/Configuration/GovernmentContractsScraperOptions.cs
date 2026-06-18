using Equibles.Worker;

namespace Equibles.GovernmentContracts.HostedService.Configuration;

public class GovernmentContractsScraperOptions : ScraperOptions
{
    /// <summary>
    /// Awards below this dollar value are ignored — federal procurement is dominated by
    /// a long tail of small actions, and only material awards move a public company.
    /// </summary>
    public decimal MinimumAwardAmount { get; set; } = 1_000_000m;

    /// <summary>
    /// Width (in days) of each action-date window fetched per API call. Narrow enough to
    /// stay under USAspending's 10,000-record deep-pagination ceiling for a window.
    /// </summary>
    public int WindowDays { get; set; } = 7;
}
