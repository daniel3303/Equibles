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

    /// <summary>
    /// Once the scan has caught up to today, how many trailing days it re-covers each cycle.
    /// USAspending publishes awards days-to-weeks after their action date, so a strict
    /// resume-after-the-frontier cursor would permanently skip any award that lands inside a
    /// window already passed. Re-scanning a trailing window each cycle picks those up; the
    /// rescan is cheap and idempotent (deduplicated by AwardUniqueKey on insert). Defaults to
    /// one window's width.
    /// </summary>
    public int RescanLookbackDays { get; set; } = 7;
}
