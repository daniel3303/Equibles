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

        // None of the guessed paths/subdomains validated. Crawl the homepage and follow the
        // investor-relations link it exposes — the IR page is often at a location guessing can't
        // reach (a Q4 / GCS host, a regional or locale path, a deeper path), and the homepage URL
        // itself is sometimes the IR site.
        return await CrawlHomepage(website, cancellationToken);
    }

    private async Task<IrDiscoveryResult> TryResolve(
        string url,
        CancellationToken cancellationToken
    )
    {
        var (html, finalUrl) = await Fetch(url, cancellationToken);
        if (html == null)
            return null;

        return BuildResult(html, finalUrl, url);
    }

    /// <summary>
    /// Fetches the page's HTML and the URL it actually landed on: rendered through the stealth
    /// sidecar when one is configured (most company/IR hosts are bot-protected, so plain HTTP
    /// would be walled), otherwise plain HTTP as the standalone fallback. Returns
    /// <c>(null, url)</c> on any miss; a single host never fails the discovery batch.
    /// </summary>
    private async Task<(string Html, string FinalUrl)> Fetch(
        string url,
        CancellationToken cancellationToken
    )
    {
        if (_stealthClient.IsEnabled)
        {
            // The stealth fetch follows redirects internally, so the requested URL is the best
            // one we have for the rendered page.
            var rendered = await _stealthClient.FetchHtml(url, cancellationToken);
            return (rendered, url);
        }

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
                // Where the request actually landed after redirects.
                var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
                return (body, finalUrl);
            }

            return (null, url);
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
            return (null, url);
        }
    }

    /// <summary>
    /// Fetches the company homepage and either validates it directly as the IR site (when the
    /// website itself is an investor.* host) or follows the investor-relations link(s) it
    /// exposes — catching IR pages at locations path/subdomain guessing can't reach.
    /// </summary>
    private async Task<IrDiscoveryResult> CrawlHomepage(
        string website,
        CancellationToken cancellationToken
    )
    {
        // EDGAR-sourced websites occasionally omit the scheme ("acme.com"); the candidate builder
        // normalizes for the guessed probes, but the homepage fetch consumes the raw value, so
        // normalize it here too or HttpClient would reject the non-absolute URL.
        var homepage = NormalizeWebsite(website);
        if (homepage == null)
            return null;

        var (html, finalUrl) = await Fetch(homepage, cancellationToken);
        if (html == null)
            return null;

        // The website itself might BE the IR site (e.g. investor.acme.com).
        var self = BuildResult(html, finalUrl, website);
        if (self != null)
            return self;

        foreach (var link in InvestorRelationsLinkExtractor.Extract(html, finalUrl))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolved = await TryResolve(link, cancellationToken);
            if (resolved != null)
            {
                _logger.LogInformation(
                    "Investor relations page resolved via homepage link {Link} for {Website}",
                    link,
                    website
                );
                return resolved;
            }
        }

        return null;
    }

    // Turns a possibly-scheme-less website into an absolute http(s) URL, or null when it can't be
    // parsed as one. Mirrors InvestorRelationsCandidateBuilder's normalization.
    private static string NormalizeWebsite(string website)
    {
        if (string.IsNullOrWhiteSpace(website))
            return null;

        var normalized = website.Trim();
        if (!normalized.Contains("://"))
            normalized = "https://" + normalized;

        return
            Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? normalized
            : null;
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
