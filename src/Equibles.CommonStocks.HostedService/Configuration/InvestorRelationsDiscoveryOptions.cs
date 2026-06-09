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
    ["investor-relations", "investors", "investor", "ir", "shareholders", "shareholder"];

    /// <summary>
    /// Subdomain prefixes probed against the registrable domain (e.g.
    /// <c>https://ir.acme.com</c>). Tried after the path candidates.
    /// </summary>
    public List<string> CandidateSubdomains { get; set; } = ["ir", "investors", "investor"];

    /// <summary>
    /// Days to wait before re-probing a stock whose last discovery attempt found no
    /// IR page. Definitive misses are stamped on the stock, so persistent misses back
    /// off for this window; transient probe errors are not stamped and retry on the
    /// next cycle.
    /// </summary>
    public int ProbeCooldownDays { get; set; } = 30;
}
