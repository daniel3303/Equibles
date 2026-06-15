using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.Logging;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Reachability-probes a candidate company-website URL and returns it in
/// normalised form when the host answers, or null when it doesn't. The probe is
/// the gate between what an <c>IWebsiteSource</c> claims and what gets persisted
/// to <c>CommonStock.Website</c>, so a stale or mis-extracted URL fails closed.
/// When a stealth sidecar is configured the host is rendered through it — a bot
/// wall (Cloudflare 403) is a live company site, not a dead host, so a plain probe
/// would wrongly discard it. Plain HTTP (the typed <see cref="HttpClient"/> from
/// <c>AddCommonStocksWorker</c>) is only the fallback when no sidecar is set.
/// </summary>
public class WebsiteProbeClient
{
    // Column ceiling for CommonStock.Website.
    private const int MaxUrlLength = 256;

    // Polite shared throttle so a discovery cycle doesn't hammer many sites at once.
    private static readonly IRateLimiter RateLimiter = new RateLimiter(
        maxRequests: 5,
        timeWindow: TimeSpan.FromSeconds(1)
    );

    private readonly HttpClient _httpClient;
    private readonly IStealthBrowserClient _stealthClient;
    private readonly ILogger<WebsiteProbeClient> _logger;

    public WebsiteProbeClient(
        HttpClient httpClient,
        IStealthBrowserClient stealthClient,
        ILogger<WebsiteProbeClient> logger
    )
    {
        _httpClient = httpClient;
        _stealthClient = stealthClient;
        _logger = logger;
    }

    /// <summary>
    /// Returns the normalised absolute URL when <paramref name="candidate"/> parses
    /// as an http(s) URL and the host serves a success response, or null otherwise.
    /// The candidate URL is what gets stored (not the post-redirect landing page):
    /// it is the company's disclosed address, and downstream IR discovery derives
    /// its probe candidates from it.
    /// </summary>
    public async Task<string> Validate(string candidate, CancellationToken cancellationToken)
    {
        var normalized = Normalize(candidate);
        if (normalized == null)
            return null;

        if (await Probe(normalized, cancellationToken))
            return normalized;

        // A "www."-prefixed investor-relations host (e.g. "www.investor.acme.com",
        // exactly as some filings disclose it) usually publishes no "www" CNAME even
        // though the bare host serves the site, so a failed "www." probe retries the
        // bare host instead of discarding a company that does have a reachable site.
        var bareHost = WithoutWww(normalized);
        if (bareHost != null && await Probe(bareHost, cancellationToken))
            return bareHost;

        return null;
    }

    /// <summary>
    /// Reachability-probes one absolute URL, returning true when the host serves a
    /// success response and false on any miss (dead host, TLS error, timeout).
    /// </summary>
    private async Task<bool> Probe(string url, CancellationToken cancellationToken)
    {
        // Render through the stealth sidecar when one is configured: it clears a bot
        // wall and returns the page, so a walled-but-live company site reads as
        // reachable instead of being discarded. A dead host fails to render -> null.
        if (_stealthClient.IsEnabled)
        {
            try
            {
                return await _stealthClient.FetchHtml(url, cancellationToken) != null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // FetchHtml is contracted to degrade to null, but a sidecar navigation
                // timeout/error can still surface as an exception. Treat it as a reachability
                // miss the same way the plain-HTTP path below does: a host that throws must not
                // bubble out of the discovery cycle and skip the definitive-miss back-off, which
                // would re-occupy the batch and starve the rest of the universe.
                _logger.LogDebug(ex, "Website stealth probe failed for {Url}", url);
                return false;
            }
        }

        try
        {
            await RateLimiter.WaitAsync();

            // Headers-read: only the status matters, so don't download the body.
            // (HEAD would be cheaper still, but enough hosts mishandle it.)
            using var response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A dead host, TLS error, or timeout is a definitive miss for this
            // candidate, not worth reporting — the next source gets its turn.
            _logger.LogDebug(ex, "Website probe failed for {Url}", url);
            return false;
        }
    }

    /// <summary>
    /// Normalises a candidate to an absolute http(s) URL within the column ceiling:
    /// trims, assumes https when the scheme is omitted (filings usually disclose
    /// "www.acme.com"), and rejects anything that doesn't parse to an http(s) host.
    /// </summary>
    public static string Normalize(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        var normalized = candidate.Trim();
        if (!normalized.Contains("://"))
            normalized = "https://" + normalized;

        if (
            !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || !uri.Host.Contains('.')
            // A userinfo part means the candidate wasn't a bare web address — e.g.
            // "mailto:ir@acme.com" parses as user "mailto:ir" at host "acme.com"
            // once the https:// prefix is applied.
            || !string.IsNullOrEmpty(uri.UserInfo)
        )
            return null;

        return normalized.Length > MaxUrlLength ? null : normalized;
    }

    /// <summary>
    /// The same absolute URL with a leading "www." stripped from its host, or null
    /// when the host doesn't start with "www." or stripping it would leave nothing
    /// host-shaped (e.g. "www.com"). Backs the probe fallback: an over-qualified
    /// host such as "www.investor.acme.com" resolves at its bare form
    /// "investor.acme.com" when the "www." variant has no DNS record. The scheme and
    /// any path are preserved unchanged.
    /// </summary>
    public static string WithoutWww(string normalizedUrl)
    {
        if (string.IsNullOrEmpty(normalizedUrl))
            return null;

        var schemeIndex = normalizedUrl.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex < 0)
            return null;

        var hostStart = schemeIndex + 3;
        if (!normalizedUrl.AsSpan(hostStart).StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            return null;

        var stripped = normalizedUrl.Remove(hostStart, 4);
        var hostEnd = stripped.IndexOfAny(['/', ':', '?', '#'], hostStart);
        var bareHost = hostEnd < 0 ? stripped[hostStart..] : stripped[hostStart..hostEnd];
        return bareHost.Contains('.') ? stripped : null;
    }
}
