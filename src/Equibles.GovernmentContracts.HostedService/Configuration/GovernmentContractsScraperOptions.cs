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
    /// Width (in days) of each action-date window fetched per API call. Kept small on purpose:
    /// a single 7-day window fired ~250 requests (deep amount-cursor paging at the $1M floor),
    /// so during one of USAspending's intermittent bad spells the odds every one of those
    /// requests survives is near zero and the whole window — the whole cycle — aborts. A 2-day
    /// window is roughly a third of the requests, far likelier to complete in a brief healthy
    /// stretch; the scan checkpoint makes the extra window count free (each completed window is
    /// durable). The amount-cursor still handles the per-window deep-pagination ceiling.
    /// </summary>
    public int WindowDays { get; set; } = 2;

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
