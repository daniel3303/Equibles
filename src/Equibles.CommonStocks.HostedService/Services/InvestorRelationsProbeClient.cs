using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.Logging;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Probes a company's candidate investor-relations URLs and returns the first one
/// that resolves to a validated IR page. When a stealth sidecar is configured (the
/// commercial deployment), every candidate is rendered through
/// <see cref="IStealthBrowserClient"/> — most IR hosts are bot-protected, so plain
/// HTTP would just be walled. Plain HTTP (the typed <see cref="HttpClient"/> from
/// <c>AddCommonStocksWorker</c>) is used only as the fallback when no sidecar is set.
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
    private readonly IStealthBrowserClient _stealthClient;
    private readonly ILogger<InvestorRelationsProbeClient> _logger;

    public InvestorRelationsProbeClient(
        HttpClient httpClient,
        IStealthBrowserClient stealthClient,
        ILogger<InvestorRelationsProbeClient> logger
    )
    {
        _httpClient = httpClient;
        _stealthClient = stealthClient;
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
        // Company/IR hosts are mostly bot-protected, so render every candidate through
        // the stealth sidecar when one is configured. Plain HTTP is only the fallback
        // for a standalone build with no sidecar.
        if (_stealthClient.IsEnabled)
            return await ResolveViaStealth(url, cancellationToken);

        try
        {
            await RateLimiter.WaitAsync();

            using var response = await _httpClient.GetAsync(url, cancellationToken);

            // Only HTML is worth reading: it can be keyword-validated. PDFs, JSON login
            // walls, etc. carry nothing useful — skip the body entirely.
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var isHtml = string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase);
            var body = isHtml ? await response.Content.ReadAsStringAsync(cancellationToken) : null;

            if (response.IsSuccessStatusCode && body != null)
            {
                // Prefer where the request actually landed after redirects, falling
                // back to the probed URL when that overruns the column ceiling.
                var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
                return BuildResult(body, finalUrl, url);
            }

            return null;
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

    private async Task<IrDiscoveryResult> ResolveViaStealth(
        string url,
        CancellationToken cancellationToken
    )
    {
        var html = await _stealthClient.FetchHtml(url, cancellationToken);
        if (html == null)
            return null;

        // The stealth fetch follows redirects internally, so the candidate is the
        // best URL we have for the rendered page.
        var result = BuildResult(html, url, url);
        if (result != null)
            _logger.LogInformation(
                "Investor relations page resolved via stealth fetch for {Url}",
                url
            );

        return result;
    }

    /// <summary>
    /// Validates rendered HTML and, when it is an IR page, builds the result with the
    /// classified platform. Uses <paramref name="preferredUrl"/>, falling back to
    /// <paramref name="fallbackUrl"/> when it overruns the column ceiling; returns
    /// null when neither fits or the page does not validate.
    /// </summary>
    private static IrDiscoveryResult BuildResult(
        string html,
        string preferredUrl,
        string fallbackUrl
    )
    {
        if (!InvestorRelationsPageValidator.IsInvestorRelationsPage(html))
            return null;

        var url = preferredUrl.Length > MaxUrlLength ? fallbackUrl : preferredUrl;
        if (url.Length > MaxUrlLength)
            return null;

        return new IrDiscoveryResult(url, IrPlatformClassifier.Classify(html));
    }
}
