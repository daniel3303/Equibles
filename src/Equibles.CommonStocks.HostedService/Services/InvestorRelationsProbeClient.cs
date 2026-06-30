using Equibles.Worker;

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
    private readonly OutboundHostGate _hostGate;
    private readonly IReadOnlyList<IInvestorRelationsPageConfirmer> _confirmers;
    private readonly ILogger<InvestorRelationsProbeClient> _logger;

    public InvestorRelationsProbeClient(
        IStealthBrowserClient stealthClient,
        OutboundHostGate hostGate,
        ILogger<InvestorRelationsProbeClient> logger,
        IEnumerable<IInvestorRelationsPageConfirmer> confirmers = null
    )
    {
        _stealthClient = stealthClient;
        _hostGate = hostGate;
        // Optional second-pass confirmers. None in a standalone OSS build (the prefilter is the only
        // gate); the commercial build registers one. A null is the no-confirmer case for the tests
        // that construct the client directly.
        _confirmers = confirmers?.ToList() ?? [];
        _logger = logger;
    }

    /// <summary>
    /// Probes <paramref name="website"/> for an investor-relations page and returns a classified
    /// <see cref="IrProbeResult"/>: <c>Found</c> with the validated URL + platform; <c>NoIrPageFound</c>
    /// when every candidate was assessed and none was an IR page; or <c>Inconclusive</c> when the
    /// stealth engine was unavailable for one or more candidates, so a real IR page may have been
    /// missed. With no sidecar configured the probe reports <c>NoIrPageFound</c> (it can't get past bot
    /// walls anyway, and re-probing won't help).
    /// </summary>
    public async Task<IrProbeResult> Discover(
        string website,
        IEnumerable<string> paths,
        IEnumerable<string> subdomains,
        CancellationToken cancellationToken
    )
    {
        if (!_stealthClient.IsEnabled)
            return IrProbeResult.NoIrPage;

        var candidates = InvestorRelationsCandidateBuilder.Build(website, paths, subdomains);

        // Render each guessed path/subdomain through the sidecar; the first that validates wins. Track
        // whether any candidate couldn't be assessed (engine unavailable) so a transient failure isn't
        // reported as a conclusive "no IR page".
        var anyTransient = false;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (page, transient) = await ProbeCandidate(candidate, candidate, cancellationToken);
            if (page != null)
                return IrProbeResult.Found(page);
            anyTransient |= transient;
        }

        // None of the guesses validated. Crawl the homepage and follow the investor-relations link it
        // exposes — the IR page is often at a location guessing can't reach (a Q4 / GCS host, a
        // regional or locale path, a deeper path), and the homepage URL itself is sometimes the IR site.
        var (homepageResult, homepageTransient) = await CrawlHomepage(website, cancellationToken);
        if (homepageResult != null)
            return IrProbeResult.Found(homepageResult);
        anyTransient |= homepageTransient;

        return anyTransient ? IrProbeResult.Inconclusive : IrProbeResult.NoIrPage;
    }

    // Renders one candidate and classifies it: a validated page; a non-validating render or a
    // definitively-absent host (both conclusive — assessed, not an IR page here); or a transient engine
    // failure that left the candidate unassessed.
    private async Task<(IrDiscoveryResult Page, bool Transient)> ProbeCandidate(
        string url,
        string fallbackUrl,
        CancellationToken cancellationToken
    )
    {
        var fetch = await FetchRendered(url, cancellationToken);
        if (fetch.Status == StealthFetchStatus.Rendered)
        {
            var page = BuildResult(fetch.Html, url, fallbackUrl);
            // A keyword-validated page must also clear the optional second-pass confirmers before it
            // counts as found. A confirmer rejection is a conclusive assessment of THIS candidate
            // (assessed, not an IR page), so the probe keeps looking rather than retrying.
            if (page == null || !await IsConfirmedIrPage(page.Url, fetch.Html, cancellationToken))
                return (null, false);
            return (page, false);
        }

        // PageUnavailable is conclusive (definitively absent); anything else is a transient engine miss.
        return (null, fetch.Status != StealthFetchStatus.PageUnavailable);
    }

    // Runs the optional second-pass confirmers on a candidate that already passed the keyword
    // prefilter. With no confirmer registered (the OSS default) every page is accepted, so behavior
    // is keyword-only. The commercial build registers a confirmer that rejects the link-dense pages
    // (sitemaps, indexes) and press releases the keyword check can't distinguish from a real IR hub.
    private async Task<bool> IsConfirmedIrPage(
        string url,
        string html,
        CancellationToken cancellationToken
    )
    {
        foreach (var confirmer in _confirmers)
        {
            if (!await confirmer.IsInvestorRelationsPage(url, html, cancellationToken))
            {
                _logger.LogDebug(
                    "Investor relations candidate rejected by page confirmer: {Url}",
                    url
                );
                return false;
            }
        }

        return true;
    }

    // Renders the page through the stealth sidecar (most IR hosts are bot-protected), returning its
    // classified outcome. Degrades an unexpected throw to a transient sidecar-unavailable result so a
    // single host never fails the discovery batch nor is wrongly written off as having no IR page.
    private async Task<StealthFetchResult> FetchRendered(
        string url,
        CancellationToken cancellationToken
    )
    {
        // Politeness gate: skip a host parked in a rate-limit cooldown, and otherwise pace requests so
        // the candidate burst doesn't trip the host's limiter. A skipped/paced host is transient — the
        // stock retries after the cooldown rather than being recorded as having no IR page.
        if (_hostGate.IsCoolingDown(url))
            return StealthFetchResult.SidecarUnavailable;
        try
        {
            await _hostGate.WaitForTurn(url, cancellationToken);
        }
        catch (HostCoolingDownException)
        {
            return StealthFetchResult.SidecarUnavailable;
        }

        try
        {
            var result = await _stealthClient.TryFetchHtml(url, cancellationToken);
            // The render landed on a rate-limit interstitial (Cloudflare 1015): cool the host down so
            // every lane stops hitting it, and treat this candidate as a transient miss.
            if (
                result.Status == StealthFetchStatus.Rendered
                && RateLimitDetector.IsRateLimited(null, result.Html)
            )
            {
                _hostGate.RecordRateLimited(url);
                return StealthFetchResult.SidecarUnavailable;
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Investor relations stealth probe failed for {Url}", url);
            return StealthFetchResult.SidecarUnavailable;
        }
    }

    /// <summary>
    /// Fetches the company homepage and either validates it directly as the IR site (when the website
    /// itself is an investor.* host) or follows the investor-relations link(s) it exposes — catching
    /// IR pages at locations path/subdomain guessing can't reach. Returns the validated page (or null)
    /// and whether any fetch was transiently unassessable.
    /// </summary>
    private async Task<(IrDiscoveryResult Result, bool Transient)> CrawlHomepage(
        string website,
        CancellationToken cancellationToken
    )
    {
        // EDGAR-sourced websites occasionally omit the scheme ("acme.com"); the candidate builder
        // normalizes for the guessed probes, but the homepage fetch consumes the raw value, so
        // normalize it here too.
        var homepage = NormalizeWebsite(website);
        if (homepage == null)
            return (null, false);

        var fetch = await FetchRendered(homepage, cancellationToken);
        if (fetch.Status != StealthFetchStatus.Rendered)
            // Couldn't load the homepage — transient unless the host is definitively absent.
            return (null, fetch.Status != StealthFetchStatus.PageUnavailable);

        var html = fetch.Html;

        // The website itself might BE the IR site (e.g. investor.acme.com). It must clear the
        // confirmers too; if it doesn't, fall through to following the homepage's IR links.
        var self = BuildResult(html, homepage, website);
        if (self != null && await IsConfirmedIrPage(self.Url, html, cancellationToken))
            return (self, false);

        var anyTransient = false;
        foreach (var link in InvestorRelationsLinkExtractor.Extract(html, homepage))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (page, transient) = await ProbeCandidate(link, link, cancellationToken);
            if (page != null)
            {
                _logger.LogInformation(
                    "Investor relations page resolved via homepage link {Link} for {Website}",
                    link,
                    website
                );
                return (page, false);
            }
            anyTransient |= transient;
        }

        return (null, anyTransient);
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
