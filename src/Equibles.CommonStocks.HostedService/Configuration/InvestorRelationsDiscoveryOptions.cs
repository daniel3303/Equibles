using Equibles.Worker;

namespace Equibles.CommonStocks.HostedService.Configuration;

public class InvestorRelationsDiscoveryOptions : ScraperOptions
{
    /// <summary>
    /// Maximum number of stocks probed per cycle. Discovery is network-bound and
    /// politely rate-limited, so a cycle works through a bounded batch and the
    /// remaining stocks are picked up on the next cycle.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Relative paths probed against the company website (e.g.
    /// <c>https://acme.com/investor-relations</c>). Tried in order; the first that
    /// resolves to a validated IR page wins.
    /// </summary>
    public List<string> CandidatePaths { get; set; } =
    [
        "investor-relations",
        "investorrelations",
        "investors",
        "investor",
        "ir",
        "shareholders",
        "shareholder",
    ];

    /// <summary>
    /// Subdomain prefixes probed against the registrable domain (e.g.
    /// <c>https://ir.acme.com</c>). Tried after the path candidates.
    /// </summary>
    public List<string> CandidateSubdomains { get; set; } =
    ["ir", "investors", "investor", "investorrelations"];

    /// <summary>
    /// Initial backoff (days) before re-probing a stock whose last attempt found no IR page. Each
    /// subsequent definitive miss doubles the wait, capped at <see cref="RetryMaxBackoffDays"/>, so a
    /// transiently-blocked site (a Cloudflare "access denied", a one-off render failure) is retried
    /// within a day instead of written off for weeks, while a persistent miss still backs off to the
    /// cap. Transient probe errors are not stamped at all and retry on the next cycle.
    /// </summary>
    public int RetryInitialBackoffDays { get; set; } = 1;

    /// <summary>
    /// Cap (days) on the exponential re-probe backoff — a miss never waits longer than this between
    /// attempts, so even a long-failing site is re-checked at least this often.
    /// </summary>
    public int RetryMaxBackoffDays { get; set; } = 15;

    /// <summary>
    /// How many candidate probes to run concurrently within a cycle. Each probe escalates to a
    /// stealth-browser render for bot-walled hosts, which can take up to the render timeout, so a
    /// serial loop made a batch with many such hosts take many minutes. Bounded to keep within the
    /// shared stealth sidecar's own concurrency cap (contended with slide/webcast capture).
    /// </summary>
    public int ProbeConcurrency { get; set; } = 6;
}
