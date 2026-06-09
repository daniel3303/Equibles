using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.Logging;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Probes a company's candidate investor-relations URLs over HTTP and returns the
/// first one that resolves to a validated IR page. Registered as a typed
/// <see cref="HttpClient"/> client (see <c>AddCommonStocksWorker</c>).
/// </summary>
public class InvestorRelationsProbeClient
{
    // Column ceiling for CommonStock.InvestorRelationsUrl.
    private const int MaxUrlLength = 256;

    // Polite shared throttle so a discovery cycle doesn't hammer many sites at once.
    private static readonly IRateLimiter RateLimiter = new RateLimiter(
        maxRequests: 5,
        timeWindow: TimeSpan.FromSeconds(1)
    );

    private readonly HttpClient _httpClient;
    private readonly ILogger<InvestorRelationsProbeClient> _logger;

    public InvestorRelationsProbeClient(
        HttpClient httpClient,
        ILogger<InvestorRelationsProbeClient> logger
    )
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Returns the validated investor-relations URL for <paramref name="website"/>
    /// together with the platform classified from its page, or null when no
    /// candidate resolves to a recognisable IR page.
    /// </summary>
    public async Task<IrDiscoveryResult> Discover(
        string website,
        IEnumerable<string> paths,
        IEnumerable<string> subdomains,
        CancellationToken cancellationToken
    )
    {
        var candidates = InvestorRelationsCandidateBuilder.Build(website, paths, subdomains);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolved = await TryResolve(candidate, cancellationToken);
            if (resolved != null)
                return resolved;
        }

        return null;
    }

    private async Task<IrDiscoveryResult> TryResolve(
        string url,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await RateLimiter.WaitAsync();

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            // Only HTML is worth keyword-validating; skip PDFs, redirects to login
            // walls that serve JSON, etc.
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase))
                return null;

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!InvestorRelationsPageValidator.IsInvestorRelationsPage(html))
                return null;

            // Prefer where the request actually landed after redirects, falling back
            // to the probed URL. Stay within the column ceiling.
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
            if (finalUrl.Length > MaxUrlLength)
                finalUrl = url;
            if (finalUrl.Length > MaxUrlLength)
                return null;

            // Classify the platform from the page we already fetched — no second request.
            var platform = IrPlatformClassifier.Classify(html);
            return new IrDiscoveryResult(finalUrl, platform);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A dead host, TLS error, or timeout on a guessed URL is expected and
            // not worth reporting — most candidates won't exist.
            _logger.LogDebug(ex, "Investor relations probe failed for {Url}", url);
            return null;
        }
    }
}
