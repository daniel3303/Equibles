using Equibles.Worker;

namespace Equibles.GovernmentContracts.HostedService.Configuration;

public class GovernmentContractsScraperOptions : ScraperOptions
{
    public GovernmentContractsScraperOptions()
    {
        // Federal contract awards publish gradually through the day; poll several times a day
        // (the base default is 24h) so new awards surface within hours of USAspending
        // publishing them instead of once daily. The source still lags the real award date by
        // days, so this is about as fresh as this dataset meaningfully gets — tune if needed.
        SleepIntervalHours = 3;
    }

    /// <summary>
    /// Awards below this dollar value are ignored — federal procurement is dominated by
    /// a long tail of small actions, and only material awards move a public company.
    /// </summary>
    public decimal MinimumAwardAmount { get; set; } = 1_000_000m;

    /// <summary>
    /// Width (in days) of each action-date window fetched per API call. A day of federal
    /// contract actions at the $1M floor is only a few hundred awards (~2–5 pages), so a
    /// 1-day window is the cheapest useful unit of work and stays small enough to finish
    /// inside a brief healthy stretch of a flaky API. Windows are not free to lose — a
    /// window aborts as a whole — so narrow beats wide: the scan checkpoint banks every
    /// completed window, making the higher window count essentially free.
    ///
    /// The earlier "a 7-day window fires ~250 requests" tuning note measured a broken
    /// query, not real volume: the client was omitting the window's date_type, so
    /// USAspending was returning every contract in force rather than those actioned.
    /// </summary>
    public int WindowDays { get; set; } = 1;

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
