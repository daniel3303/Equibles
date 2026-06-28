namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Probes a company's candidate investor-relations URLs and returns the first one that resolves to a
/// validated IR page. All fetches go through the stealth sidecar (<see cref="IStealthBrowserClient"/>):
/// virtually every IR host is bot-protected, so a plain-HTTP pass only ever returned a challenge page
/// that failed validation — it found nothing while adding ~100s of dead-host timeouts per stock before
/// the stealth pass even started. With no sidecar configured the client is a no-op (a build without a
/// stealth browser can't get past the bot walls anyway), so discovery simply finds nothing.
/// </summary>
public class InvestorRelationsProbeClient
{
    // Column ceiling for CommonStock.InvestorRelationsUrl.
    private const int MaxUrlLength = 256;

    private readonly IStealthBrowserClient _stealthClient;
    private readonly ILogger<InvestorRelationsProbeClient> _logger;

    public InvestorRelationsProbeClient(
        IStealthBrowserClient stealthClient,
        ILogger<InvestorRelationsProbeClient> logger
    )
    {
        _stealthClient = stealthClient;
        _logger = logger;
    }

    /// <summary>
    /// Returns the validated investor-relations URL for <paramref name="website"/> together with the
    /// platform classified from its page, or null when no candidate resolves to a recognisable IR
    /// page (or no sidecar is configured).
    /// </summary>
    public async Task<IrDiscoveryResult> Discover(
        string website,
        IEnumerable<string> paths,
        IEnumerable<string> subdomains,
        CancellationToken cancellationToken
    )
    {
        if (!_stealthClient.IsEnabled)
            return null;

        var candidates = InvestorRelationsCandidateBuilder.Build(website, paths, subdomains);

        // Render each guessed path/subdomain through the sidecar; the first that validates wins.
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolved = await TryResolve(candidate, cancellationToken);
            if (resolved != null)
                return resolved;
        }

        // None of the guesses validated. Crawl the homepage and follow the investor-relations link it
        // exposes — the IR page is often at a location guessing can't reach (a Q4 / GCS host, a
        // regional or locale path, a deeper path), and the homepage URL itself is sometimes the IR site.
        return await CrawlHomepage(website, cancellationToken);
    }

    private async Task<IrDiscoveryResult> TryResolve(
        string url,
        CancellationToken cancellationToken
    )
    {
        var html = await FetchRendered(url, cancellationToken);
        return html == null ? null : BuildResult(html, url, url);
    }

    // Renders the page through the stealth sidecar (most IR hosts are bot-protected). Degrades to
    // null on any error so a single host never fails the discovery batch.
    private async Task<string> FetchRendered(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await _stealthClient.FetchHtml(url, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // FetchHtml is contracted to degrade to null, but a sidecar navigation timeout/error can
            // still surface as an exception. Swallow it: a single host that throws must not bubble out
            // of the probe, or the caller skips the definitive-miss back-off and the stock re-occupies
            // every batch, starving the rest of the universe.
            _logger.LogDebug(ex, "Investor relations stealth probe failed for {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Fetches the company homepage and either validates it directly as the IR site (when the website
    /// itself is an investor.* host) or follows the investor-relations link(s) it exposes — catching
    /// IR pages at locations path/subdomain guessing can't reach.
    /// </summary>
    private async Task<IrDiscoveryResult> CrawlHomepage(
        string website,
        CancellationToken cancellationToken
    )
    {
        // EDGAR-sourced websites occasionally omit the scheme ("acme.com"); the candidate builder
        // normalizes for the guessed probes, but the homepage fetch consumes the raw value, so
        // normalize it here too.
        var homepage = NormalizeWebsite(website);
        if (homepage == null)
            return null;

        var html = await FetchRendered(homepage, cancellationToken);
        if (html == null)
            return null;

        // The website itself might BE the IR site (e.g. investor.acme.com).
        var self = BuildResult(html, homepage, website);
        if (self != null)
            return self;

        foreach (var link in InvestorRelationsLinkExtractor.Extract(html, homepage))
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
    /// Validates rendered HTML and, when it is an IR page, builds the result with the classified
    /// platform. Uses <paramref name="preferredUrl"/>, falling back to <paramref name="fallbackUrl"/>
    /// when it overruns the column ceiling; returns null when neither fits or the page does not validate.
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
