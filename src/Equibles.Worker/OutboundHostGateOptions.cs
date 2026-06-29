namespace Equibles.Worker;

/// <summary>
/// Tuning for <see cref="OutboundHostGate"/> — the shared politeness gate for outbound scraping.
/// </summary>
public class OutboundHostGateOptions
{
    /// <summary>
    /// Minimum spacing between requests to the same registrable domain. Smooths a burst (e.g. the IR
    /// probe's path/subdomain candidates) into paced requests so a host's rate limiter isn't tripped.
    /// </summary>
    public int MinIntervalMilliseconds { get; set; } = 1500;

    /// <summary>
    /// How long to park a host after it rate-limited us (Cloudflare 1015 / HTTP 429) — every lane skips
    /// the host for this window so we stop hammering an IP-banned host. A few hours by default.
    /// </summary>
    public int CooldownMinutes { get; set; } = 180;
}
