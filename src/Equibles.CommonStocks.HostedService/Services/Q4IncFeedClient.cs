using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.Logging;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Fetches the RSS feeds published by a Q4 Inc-hosted IR site. Registered as a
/// typed <see cref="HttpClient"/> client (see <c>AddCommonStocksWorker</c>).
/// Returns null on any non-success or non-XML response so the caller can skip the
/// feed without treating an outage as data.
/// </summary>
public class Q4IncFeedClient
{
    // Relative feed paths a Q4 Inc site exposes off its IR base URL.
    public const string NewsFeedPath = "rss/pressrelease.aspx";
    public const string EventsFeedPath = "rss/event.aspx";

    // Polite shared throttle across feed fetches.
    private static readonly IRateLimiter RateLimiter = new RateLimiter(
        maxRequests: 5,
        timeWindow: TimeSpan.FromSeconds(1)
    );

    private readonly HttpClient _httpClient;
    private readonly IStealthBrowserClient _stealthClient;
    private readonly ILogger<Q4IncFeedClient> _logger;

    public Q4IncFeedClient(
        HttpClient httpClient,
        IStealthBrowserClient stealthClient,
        ILogger<Q4IncFeedClient> logger
    )
    {
        _httpClient = httpClient;
        _stealthClient = stealthClient;
        _logger = logger;
    }

    /// <summary>
    /// Combines an IR base URL with a feed path and returns the feed body, or null
    /// when the feed is missing, errors, or is not XML. When a bot-protected host
    /// answers with a challenge instead of the feed and the stealth path is enabled,
    /// the feed is pulled through the cleared stealth session instead.
    /// </summary>
    public async Task<string> Fetch(
        string irBaseUrl,
        string feedPath,
        CancellationToken cancellationToken
    )
    {
        if (!TryBuildFeedUrl(irBaseUrl, feedPath, out var feedUrl))
            return null;

        try
        {
            await RateLimiter.WaitAsync();

            using var response = await _httpClient.GetAsync(feedUrl, cancellationToken);

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var isXml =
                mediaType != null && mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase);

            if (response.IsSuccessStatusCode && isXml)
                return await response.Content.ReadAsStringAsync(cancellationToken);

            // A bot wall answers the feed with an HTML challenge stub (or a hard
            // block) rather than XML. When the stealth path is on and the body
            // carries a challenge signature, pull the feed through the cleared
            // stealth session; otherwise this is a genuine miss.
            if (_stealthClient.IsEnabled)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (StealthChallengeDetector.IsChallenge(body))
                    return await _stealthClient.FetchRaw(feedUrl, cancellationToken);
            }

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Investor relations feed fetch failed for {Url}", feedUrl);
            return null;
        }
    }

    private static bool TryBuildFeedUrl(string irBaseUrl, string feedPath, out string feedUrl)
    {
        feedUrl = null;
        if (string.IsNullOrWhiteSpace(irBaseUrl))
            return false;

        // The IR base is the discovered IR landing page; feeds live at the site root,
        // so resolve the path against the origin rather than the landing path.
        if (!Uri.TryCreate(irBaseUrl, UriKind.Absolute, out var baseUri))
            return false;

        feedUrl = new Uri(baseUri, "/" + feedPath).ToString();
        return true;
    }
}
