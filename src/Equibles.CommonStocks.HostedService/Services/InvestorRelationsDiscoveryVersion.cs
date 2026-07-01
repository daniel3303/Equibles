namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// The current generation of the investor-relations page discovery logic — the candidate paths and
/// subdomains probed, the homepage crawl, the stealth render, and the page validation, taken
/// together. Every <see cref="Equibles.CommonStocks.Data.Models.CommonStock"/> records the version
/// it was last probed under.
///
/// A stock that came up a definitive miss backs off for the cooldown so it isn't re-probed every
/// cycle. But that cooldown also delays the benefit of a probe improvement: a smarter probe can't
/// re-examine the backlog of misses until each one's cooldown independently expires. Bumping this
/// constant makes every stock stamped with an older version eligible for re-probe immediately, so an
/// improvement reaches the whole backlog on deploy rather than over the cooldown window. After the
/// re-probe the stock is re-stamped to the current version, so it is reconsidered only when the
/// version advances again.
///
/// BUMP THIS by one in the same change that ships a discovery improvement that could newly find an IR
/// page the prior generation missed (a new candidate path/subdomain, a better crawl, a render fix,
/// looser validation).
/// </summary>
public static class InvestorRelationsDiscoveryVersion
{
    /// <summary>
    /// v1: the discovery logic as it stood at introduction (plain-HTTP-first with stealth fallback,
    /// path + subdomain guessing, homepage crawl, platform classification). The column defaults the
    /// pre-existing corpus to 0, so every stock probed before this generation is reconsidered once
    /// against it.
    ///
    /// v2: subdomain-first candidate ordering, press-release (<c>og:type=article</c>) rejection, and
    /// the second-pass page confirmers all shipped without a bump, so the backlog of misses was never
    /// re-examined against them — this bump re-sweeps it. Also the first generation where a transient
    /// (engine-unavailable) miss leaves the stamped version untouched.
    /// </summary>
    public const int Current = 2;
}
