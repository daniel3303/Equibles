using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.Logging;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Probes a company's candidate investor-relations URLs over HTTP and returns the
/// first one that resolves to a validated IR page. Registered as a typed
/// <see cref="HttpClient"/> client (see <c>AddCommonStocksWorker</c>). When a host
/// answers with a bot-protection challenge instead of the page and the stealth
/// fetch path is enabled, the candidate is re-fetched through
/// <see cref="IStealthBrowserClient"/> and the rendered page is validated instead.
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
        try
        {
            await RateLimiter.WaitAsync();

            string body;
            using (var response = await _httpClient.GetAsync(url, cancellationToken))
            {
                // Only HTML is worth reading: it can be keyword-validated, and a bot
                // wall serves its challenge as HTML too. PDFs, JSON login walls, etc.
                // carry nothing useful — skip the body entirely.
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                var isHtml = string.Equals(
                    mediaType,
                    "text/html",
                    StringComparison.OrdinalIgnoreCase
                );
                body = isHtml ? await response.Content.ReadAsStringAsync(cancellationToken) : null;

                if (response.IsSuccessStatusCode && body != null)
                {
                    // Prefer where the request actually landed after redirects, falling
                    // back to the probed URL when that overruns the column ceiling.
                    var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
                    var direct = BuildResult(body, finalUrl, url);
                    if (direct != null)
                        return direct;
                }
            }

            // Bot wall: a 2xx challenge stub that didn't validate, or a hard block
            // (e.g. Akamai 403 "Access Denied") whose body still carries the vendor
            // signature. When the stealth path is configured, render the candidate
            // and validate that instead — the only way a walled host ever resolves.
            if (_stealthClient.IsEnabled && StealthChallengeDetector.IsChallenge(body))
                return await ResolveViaStealth(url, cancellationToken);

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
