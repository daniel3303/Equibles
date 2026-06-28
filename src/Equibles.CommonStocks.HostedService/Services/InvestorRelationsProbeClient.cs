using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.Logging;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Probes a company's candidate investor-relations URLs and returns the first one
/// that resolves to a validated IR page. Tries plain HTTP first (the typed
/// <see cref="HttpClient"/> from <c>AddCommonStocksWorker</c>): it is cheap and never touches the
/// shared stealth sidecar, which is contended with the slide/webcast capture. Only when the
/// plain-HTTP pass finds nothing AND a sidecar is configured does it run a second pass that renders
/// every candidate through <see cref="IStealthBrowserClient"/> — catching the bot-protected hosts
/// (where plain HTTP gets a challenge page that fails validation) and the JS-rendered homepages
/// whose IR nav links aren't in the static HTML. Escalating only on a plain-HTTP miss keeps sidecar
/// load proportional to the hard cases instead of every stock.
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

        // TEMP DEBUG INSTRUMENTATION (#ir-probe-timing): per-pass timing to locate the discovery
        // sweep stall. Remove once the bottleneck is fixed.
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Pass 1 — plain HTTP. Cheap, polite, and never touches the shared stealth sidecar; most
        // IR pages that aren't bot-walled resolve here.
        var direct = await Probe(candidates, website, useSidecar: false, cancellationToken);
        _logger.LogInformation(
            "IR-PROBE-TIMING pass1(plain) {Website}: {Result} in {Ms}ms ({Candidates} candidates)",
            website,
            direct != null ? "HIT" : "miss",
            sw.ElapsedMilliseconds,
            candidates.Count
        );
        if (direct != null)
            return direct;

        // Pass 2 — stealth render, only when a sidecar is configured and only after plain HTTP came
        // up empty. Catches the bot-protected hosts (plain HTTP saw a challenge page that failed
        // validation) and the JS-rendered homepages whose IR nav links aren't in the static HTML.
        if (_stealthClient.IsEnabled)
        {
            sw.Restart();
            var rendered = await Probe(candidates, website, useSidecar: true, cancellationToken);
            _logger.LogInformation(
                "IR-PROBE-TIMING pass2(stealth) {Website}: {Result} in {Ms}ms",
                website,
                rendered != null ? "HIT" : "miss",
                sw.ElapsedMilliseconds
            );
            return rendered;
        }

        return null;
    }

    /// <summary>
    /// Runs one discovery pass over the guessed candidates and then the homepage crawl, all over
    /// the requested transport (plain HTTP or the stealth sidecar). Returns the first validated IR
    /// page, or null when nothing resolves in this pass.
    /// </summary>
    private async Task<IrDiscoveryResult> Probe(
        IReadOnlyList<string> candidates,
        string website,
        bool useSidecar,
        CancellationToken cancellationToken
    )
    {
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolved = await TryResolve(candidate, useSidecar, cancellationToken);
            if (resolved != null)
                return resolved;
        }

        // None of the guessed paths/subdomains validated. Crawl the homepage and follow the
        // investor-relations link it exposes — the IR page is often at a location guessing can't
        // reach (a Q4 / GCS host, a regional or locale path, a deeper path), and the homepage URL
        // itself is sometimes the IR site.
        return await CrawlHomepage(website, useSidecar, cancellationToken);
    }

    private async Task<IrDiscoveryResult> TryResolve(
        string url,
        bool useSidecar,
        CancellationToken cancellationToken
    )
    {
        var (html, finalUrl) = await Fetch(url, useSidecar, cancellationToken);
        if (html == null)
            return null;

        return BuildResult(html, finalUrl, url);
    }

    /// <summary>
    /// Fetches the page's HTML and the URL it actually landed on, over the requested transport: the
    /// stealth sidecar when <paramref name="useSidecar"/> is set, otherwise plain HTTP. Returns
    /// <c>(null, url)</c> on any miss; a single host never fails the discovery batch.
    /// </summary>
    private Task<(string Html, string FinalUrl)> Fetch(
        string url,
        bool useSidecar,
        CancellationToken cancellationToken
    ) => useSidecar ? FetchRendered(url, cancellationToken) : FetchPlain(url, cancellationToken);

    // Renders the page through the stealth sidecar (most IR hosts are bot-protected). The stealth
    // fetch follows redirects internally, so the requested URL is the best one we have.
    private async Task<(string Html, string FinalUrl)> FetchRendered(
        string url,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var rendered = await _stealthClient.FetchHtml(url, cancellationToken);
            return (rendered, url);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // FetchHtml is contracted to degrade to null, but a sidecar navigation timeout/error
            // can still surface as an exception. Swallow it the same way the plain-HTTP path does:
            // a single host that throws must not bubble out of the probe, or the caller skips the
            // definitive-miss back-off and the stock re-occupies every batch, starving the rest of
            // the universe.
            _logger.LogDebug(ex, "Investor relations stealth probe failed for {Url}", url);
            return (null, url);
        }
    }

    // Fetches the page over plain HTTP, politely rate-limited. Only HTML is read (it can be
    // keyword-validated); a non-HTML body, an error status, or a dead host is a miss.
    private async Task<(string Html, string FinalUrl)> FetchPlain(
        string url,
        CancellationToken cancellationToken
    )
    {
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
    /// exposes — catching IR pages at locations path/subdomain guessing can't reach. Uses the same
    /// transport (<paramref name="useSidecar"/>) as the pass it runs in.
    /// </summary>
    private async Task<IrDiscoveryResult> CrawlHomepage(
        string website,
        bool useSidecar,
        CancellationToken cancellationToken
    )
    {
        // EDGAR-sourced websites occasionally omit the scheme ("acme.com"); the candidate builder
        // normalizes for the guessed probes, but the homepage fetch consumes the raw value, so
        // normalize it here too or HttpClient would reject the non-absolute URL.
        var homepage = NormalizeWebsite(website);
        if (homepage == null)
            return null;

        var (html, finalUrl) = await Fetch(homepage, useSidecar, cancellationToken);
        if (html == null)
            return null;

        // The website itself might BE the IR site (e.g. investor.acme.com).
        var self = BuildResult(html, finalUrl, website);
        if (self != null)
            return self;

        foreach (var link in InvestorRelationsLinkExtractor.Extract(html, finalUrl))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolved = await TryResolve(link, useSidecar, cancellationToken);
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
