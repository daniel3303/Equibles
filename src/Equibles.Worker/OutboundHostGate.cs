using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Worker;

/// <summary>
/// Process-wide politeness gate for outbound scraping. Two jobs, both keyed by registrable domain:
/// <list type="bullet">
/// <item>throttle — space requests to a host by <see cref="OutboundHostGateOptions.MinIntervalMilliseconds"/>
/// so a burst from our single egress IP (e.g. the IR probe's 11 path/subdomain candidates) doesn't trip
/// the host's rate limiter;</item>
/// <item>cooldown — when a host DID rate-limit us (Cloudflare 1015 / HTTP 429), park it for
/// <see cref="OutboundHostGateOptions.CooldownMinutes"/> so every lane stops hitting it.</item>
/// </list>
/// Registered as a singleton and shared across all scraper lanes (IR discovery, webcast/slides capture,
/// IR-flow) because the ban is IP-wide. In-memory: a cooldown does not survive a worker restart, which
/// is acceptable — the throttle prevents re-tripping it quickly anyway.
/// </summary>
public sealed class OutboundHostGate
{
    private readonly OutboundHostGateOptions _options;
    private readonly ILogger<OutboundHostGate> _logger;
    private readonly ConcurrentDictionary<string, HostState> _hosts = new();

    public OutboundHostGate(
        IOptions<OutboundHostGateOptions> options,
        ILogger<OutboundHostGate> logger
    )
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Waits until a request to <paramref name="url"/>'s host is allowed (at least the min interval
    /// since the last one), or throws <see cref="HostCoolingDownException"/> when the host is parked in
    /// a rate-limit cooldown. Call immediately before each outbound request.
    /// </summary>
    public async Task WaitForTurn(string url, CancellationToken cancellationToken)
    {
        var key = HostKey(url);
        if (key == null)
            return;

        var state = _hosts.GetOrAdd(key, _ => new HostState());

        ThrowIfCoolingDown(key, state);

        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            // Re-check under the lock: another caller may have recorded a cooldown while we queued.
            ThrowIfCoolingDown(key, state);

            var minInterval = TimeSpan.FromMilliseconds(
                Math.Max(0, _options.MinIntervalMilliseconds)
            );
            var elapsed = DateTimeOffset.UtcNow - state.LastRequest;
            if (elapsed < minInterval)
                await Task.Delay(minInterval - elapsed, cancellationToken);

            state.LastRequest = DateTimeOffset.UtcNow;
        }
        finally
        {
            state.Gate.Release();
        }
    }

    /// <summary>Parks <paramref name="url"/>'s host for the cooldown window after it rate-limited us.</summary>
    public void RecordRateLimited(string url)
    {
        var key = HostKey(url);
        if (key == null)
            return;

        var state = _hosts.GetOrAdd(key, _ => new HostState());
        var until = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _options.CooldownMinutes));
        state.CooldownUntil = until;
        _logger.LogWarning(
            "Outbound host {Host} rate-limited us; cooling down until {Until:u}",
            key,
            until
        );
    }

    /// <summary>True when the host is currently parked in a rate-limit cooldown.</summary>
    public bool IsCoolingDown(string url)
    {
        var key = HostKey(url);
        return key != null
            && _hosts.TryGetValue(key, out var state)
            && state.CooldownUntil is { } until
            && DateTimeOffset.UtcNow < until;
    }

    private static void ThrowIfCoolingDown(string key, HostState state)
    {
        if (state.CooldownUntil is { } until && DateTimeOffset.UtcNow < until)
            throw new HostCoolingDownException(key, until);
    }

    /// <summary>
    /// Registrable-domain key: a rate-limit ban is per Cloudflare zone (apex), so investors.bjs.com and
    /// bjs.com share one cooldown. Approximated as the last two labels — correct for the .com/.net hosts
    /// that dominate IR; an over-broad key on a multi-part public suffix only throttles slightly more,
    /// never less, so it never lets a burst through.
    /// </summary>
    internal static string HostKey(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return null;

        var host = uri.Host.ToLowerInvariant();
        var labels = host.Split('.');
        return labels.Length <= 2 ? host : $"{labels[^2]}.{labels[^1]}";
    }

    private sealed class HostState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public DateTimeOffset LastRequest { get; set; } = DateTimeOffset.MinValue;
        public DateTimeOffset? CooldownUntil { get; set; }
    }
}
