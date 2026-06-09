using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.Logging;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Fetches the RSS feeds published by a Nasdaq IR Insight-hosted IR site. Registered
/// as a typed <see cref="HttpClient"/> client (see <c>AddCommonStocksWorker</c>).
/// Returns null on any non-success or non-XML response so the caller can skip the
/// feed without treating an outage as data.
/// </summary>
public class NasdaqIrInsightFeedClient
{
    // Relative feed paths a Nasdaq IR Insight site exposes off its IR base URL.
    public const string NewsFeedPath = "rss/news-releases.xml";
    public const string EventsFeedPath = "rss/events.xml";

    // Polite shared throttle across feed fetches.
    private static readonly IRateLimiter RateLimiter = new RateLimiter(
        maxRequests: 5,
        timeWindow: TimeSpan.FromSeconds(1)
    );

    private readonly HttpClient _httpClient;
    private readonly ILogger<NasdaqIrInsightFeedClient> _logger;

    public NasdaqIrInsightFeedClient(
        HttpClient httpClient,
        ILogger<NasdaqIrInsightFeedClient> logger
    )
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Combines an IR base URL with a feed path and returns the feed body, or null
    /// when the feed is missing, errors, or is not XML.
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
            if (!response.IsSuccessStatusCode)
                return null;

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType == null || !mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
                return null;

            return await response.Content.ReadAsStringAsync(cancellationToken);
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
