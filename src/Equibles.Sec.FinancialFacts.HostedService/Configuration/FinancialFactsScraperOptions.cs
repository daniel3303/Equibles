using Equibles.Worker;

namespace Equibles.Sec.FinancialFacts.HostedService.Configuration;

public class FinancialFactsScraperOptions : ScraperOptions
{
    /// <summary>
    /// A company whose facts were checked within this window is skipped by the
    /// next cycle. The walk itself gives no such guarantee: the worker restarts
    /// its full sweep whenever the host restarts (every deploy), and each visit
    /// downloads the company's complete Company Facts JSON before the
    /// LastFiledDateSeen checkpoint can say "nothing new" — so without this
    /// window a deploy-heavy day multiplies the heaviest SEC walker by the
    /// number of restarts. Kept below the 24h default sleep so a completed
    /// cycle plus a full sleep still re-checks every company.
    /// </summary>
    public int RecheckIntervalHours { get; set; } = 20;
}
