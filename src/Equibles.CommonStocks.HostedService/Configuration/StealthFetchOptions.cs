namespace Equibles.CommonStocks.HostedService.Configuration;

/// <summary>
/// Configuration for the optional stealth-browser fetch fallback, used when an IR
/// host answers a plain HTTP request with a bot-protection challenge instead of the
/// page. Bound from the <c>InvestorRelationsDiscovery:StealthFetch</c> section.
/// Disabled by default, so the stock build behaves exactly as before and carries no
/// dependency on the sidecar.
/// </summary>
public class StealthFetchOptions
{
    /// <summary>
    /// Whether the stealth fallback is active. When false (the default), a bot-
    /// challenge response is recorded as a miss, exactly as before.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Chrome DevTools Protocol endpoint of the stealth-browser sidecar (e.g.
    /// <c>http://cloakbrowser:9222</c>). Required when <see cref="Enabled"/> is true;
    /// an empty value keeps the fallback inert.
    /// </summary>
    public string SidecarUrl { get; set; }

    /// <summary>
    /// Per-page render timeout in seconds. A walled page often has to clear its
    /// challenge before the real content loads, so this is more generous than the
    /// plain HTTP probe timeout.
    /// </summary>
    public int RenderTimeoutSeconds { get; set; } = 45;

    /// <summary>
    /// Maximum number of concurrent stealth renders. Stealth fetches are expensive
    /// and politeness-sensitive, so they are bounded well below the plain-HTTP rate
    /// to avoid escalating a solvable challenge into a hard block.
    /// </summary>
    public int MaxConcurrency { get; set; } = 2;
}
