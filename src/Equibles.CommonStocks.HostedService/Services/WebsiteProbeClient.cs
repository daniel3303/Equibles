using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.Logging;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Reachability-probes a candidate company-website URL and returns it in
/// normalised form when the host answers, or null when it doesn't. The probe is
/// the gate between what an <c>IWebsiteSource</c> claims and what gets persisted
/// to <c>CommonStock.Website</c>, so a stale or mis-extracted URL fails closed.
/// Registered as a typed <see cref="HttpClient"/> client (see
/// <c>AddCommonStocksWorker</c>).
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
    private readonly ILogger<WebsiteProbeClient> _logger;

    public WebsiteProbeClient(HttpClient httpClient, ILogger<WebsiteProbeClient> logger)
    {
        _httpClient = httpClient;
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

        try
        {
            await RateLimiter.WaitAsync();

            // Headers-read: only the status matters, so don't download the body.
            // (HEAD would be cheaper still, but enough hosts mishandle it.)
            using var response = await _httpClient.GetAsync(
                normalized,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            return response.IsSuccessStatusCode ? normalized : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A dead host, TLS error, or timeout is a definitive miss for this
            // candidate, not worth reporting — the next source gets its turn.
            _logger.LogDebug(ex, "Website probe failed for {Url}", normalized);
            return null;
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
}
