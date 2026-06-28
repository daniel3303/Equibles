namespace Equibles.CommonStocks.HostedService.Configuration;

/// <summary>
/// Configuration for the stealth-browser fetch path. Company websites and IR pages
/// are fetched through the sidecar rather than plain HTTP, because most are bot-
/// protected; plain HTTP is only the fallback when no sidecar is configured. Bound
/// from the <c>InvestorRelationsDiscovery:StealthFetch</c> section. The stealth path
/// is active whenever <see cref="SidecarUrl"/> is set — there is no separate on/off
/// flag — so an empty value (the default for a standalone build) keeps it off and
/// discovery falls back to plain HTTP.
/// </summary>
public class StealthFetchOptions
{
    /// <summary>
    /// Chrome DevTools Protocol endpoint of the stealth-browser sidecar (e.g.
    /// <c>http://cloakbrowser:9222</c>). When set, company/IR fetches go through the
    /// sidecar; when empty, the stealth path is off and discovery uses plain HTTP.
    /// </summary>
    public string SidecarUrl { get; set; }

    /// <summary>
    /// Per-page render timeout in seconds. A walled page often has to clear its
    /// challenge before the real content loads, so this is more generous than the
    /// plain HTTP probe timeout.
    /// </summary>
    public int RenderTimeoutSeconds { get; set; } = 45;

    /// <summary>
    /// Maximum number of concurrent stealth renders. Stealth renders are expensive and
    /// memory-heavy, so this caps how many run at once; raise it (with the sidecar's
    /// memory) to keep a full-universe discovery sweep feasible now that every company/
    /// IR fetch goes through the sidecar.
    /// </summary>
    public int MaxConcurrency { get; set; } = 6;

    /// <summary>
    /// Timeout (seconds) for the CDP connect to the sidecar. ConnectOverCDPAsync has no built-in
    /// timeout, so a wedged or over-loaded sidecar can leave a connect hanging forever, holding a
    /// concurrency slot and — enough of them — stalling the whole discovery sweep. On timeout the
    /// fetch degrades to a miss and the stock is re-probed later.
    /// </summary>
    public int ConnectTimeoutSeconds { get; set; } = 20;
}
