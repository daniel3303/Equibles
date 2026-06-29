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
    /// Initial backoff (days) before re-probing a stock whose last attempt <em>conclusively</em> found
    /// no IR page (every candidate was assessed and none validated). Each subsequent conclusive miss
    /// doubles the wait, capped at <see cref="RetryMaxBackoffDays"/>, so a persistent miss settles at
    /// the cap. An <em>inconclusive</em> attempt — the stealth engine was unavailable for a candidate —
    /// uses the shorter <see cref="RetryTransientBackoffHours"/> instead, so a stock with a reachable IR
    /// page isn't exiled for weeks over one bad sidecar moment.
    /// </summary>
    public int RetryInitialBackoffDays { get; set; } = 1;

    /// <summary>
    /// Cap (days) on the exponential re-probe backoff — a conclusive miss never waits longer than this
    /// between attempts, so even a long-failing site is re-checked at least this often.
    /// </summary>
    public int RetryMaxBackoffDays { get; set; } = 15;

    /// <summary>
    /// Backoff (hours) before re-probing a stock whose last attempt was <em>inconclusive</em> — the
    /// stealth engine was unavailable for a candidate, so a real IR page may have been missed. Short and
    /// fixed (it does not escalate): long enough not to re-occupy a batch slot every cycle, short enough
    /// that a transiently-unreachable IR page is recovered within the day rather than after the
    /// multi-day conclusive-miss schedule.
    /// </summary>
    public int RetryTransientBackoffHours { get; set; } = 6;

    /// <summary>
    /// How many candidate probes to run concurrently within a cycle. Each probe escalates to a
    /// stealth-browser render for bot-walled hosts, which can take up to the render timeout, so a
    /// serial loop made a batch with many such hosts take many minutes. Bounded to keep within the
    /// shared stealth sidecar's own concurrency cap (contended with slide/webcast capture).
    /// </summary>
    public int ProbeConcurrency { get; set; } = 6;
}
