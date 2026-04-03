using System.Net;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Equibles.Integrations.Yahoo.Models.Responses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Equibles.Integrations.Yahoo;

[Service(ServiceLifetime.Scoped, typeof(IYahooFinanceClient))]
public class YahooFinanceClient : IYahooFinanceClient {
    private const string ChartBaseUrl = "https://query1.finance.yahoo.com/v8/finance/chart";
    private const string QuoteSummaryBaseUrl = "https://query1.finance.yahoo.com/v10/finance/quoteSummary";
    private const string CrumbUrl = "https://query1.finance.yahoo.com/v1/test/getcrumb";
    private const string CookieUrl = "https://fc.yahoo.com/";
    private const int MaxRetries = 3;
    private const int SessionLifetimeMinutes = 30;
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    // Yahoo has no documented limit; community reports ~60 req/min triggers blocking
    private static readonly IRateLimiter RateLimiter = new Common.RateLimiter.RateLimiter(
        maxRequests: 40, timeWindow: TimeSpan.FromMinutes(1));

    private static readonly SemaphoreSlim SessionSemaphore = new(1, 1);
    private static string _cachedCrumb;
    private static string _cachedCookieHeader;
    private static DateTime _sessionExpiry = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

    private static readonly DateTimeOffset UnixEpoch = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly HttpClient _httpClient;
    private readonly ILogger<YahooFinanceClient> _logger;

    public YahooFinanceClient(HttpClient httpClient, ILogger<YahooFinanceClient> logger) {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<HistoricalPrice>> GetHistoricalPrices(string ticker, DateOnly startDate, DateOnly endDate) {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
        var period1 = ToUnixTimestamp(startDate);
        var period2 = ToUnixTimestamp(endDate.AddDays(1)); // inclusive end

        var url = $"{ChartBaseUrl}/{Uri.EscapeDataString(ticker)}" +
                  $"?period1={period1}&period2={period2}&interval=1d";

        var content = await SendWithRetry(url);
        var response = JsonConvert.DeserializeObject<YahooChartResponse>(content);

        var result = response?.Chart?.Result?.FirstOrDefault();
        if (result?.Timestamp == null || result.Timestamp.Count == 0) return [];

        var quote = result.Indicators?.Quote?.FirstOrDefault();
        if (quote == null) return [];

        var adjCloseList = result.Indicators?.AdjClose?.FirstOrDefault()?.AdjustedClose;
        var prices = new List<HistoricalPrice>();

        for (var i = 0; i < result.Timestamp.Count; i++) {
            // Skip entries with missing OHLC data (market holidays, etc.)
            if (quote.Close[i] == null) continue;

            prices.Add(new HistoricalPrice {
                Date = FromUnixTimestamp(result.Timestamp[i]),
                Open = Math.Round(quote.Open[i] ?? 0, 4),
                High = Math.Round(quote.High[i] ?? 0, 4),
                Low = Math.Round(quote.Low[i] ?? 0, 4),
                Close = Math.Round(quote.Close[i].Value, 4),
                AdjustedClose = adjCloseList != null && i < adjCloseList.Count
                    ? Math.Round(adjCloseList[i] ?? 0, 4)
                    : Math.Round(quote.Close[i].Value, 4),
                Volume = quote.Volume[i] ?? 0,
            });
        }

        _logger.LogDebug("Fetched {Count} historical prices for {Ticker}", prices.Count, ticker);
        return prices;
    }

    public async Task<List<RecommendationTrend>> GetRecommendationTrends(string ticker) {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
        var url = $"{QuoteSummaryBaseUrl}/{Uri.EscapeDataString(ticker)}?modules=recommendationTrend";

        var content = await SendWithRetry(url);
        var response = JsonConvert.DeserializeObject<YahooQuoteSummaryResponse>(content);

        var trends = response?.QuoteSummary?.Result?.FirstOrDefault()
            ?.RecommendationTrend?.Trend;

        if (trends == null || trends.Count == 0) return [];

        var result = trends.Select(t => new RecommendationTrend {
            Period = t.Period,
            StrongBuy = t.StrongBuy,
            Buy = t.Buy,
            Hold = t.Hold,
            Sell = t.Sell,
            StrongSell = t.StrongSell,
        }).ToList();

        _logger.LogDebug("Fetched {Count} recommendation trends for {Ticker}", result.Count, ticker);
        return result;
    }

    public async Task<KeyStatistics> GetKeyStatistics(string ticker) {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
        var url = $"{QuoteSummaryBaseUrl}/{Uri.EscapeDataString(ticker)}?modules=defaultKeyStatistics";

        var content = await SendWithRetry(url);
        var response = JsonConvert.DeserializeObject<YahooQuoteSummaryResponse>(content);

        var stats = response?.QuoteSummary?.Result?.FirstOrDefault()?.DefaultKeyStatistics;
        if (stats == null) return null;

        return new KeyStatistics {
            SharesOutstanding = stats.SharesOutstanding?.Raw ?? 0
        };
    }

    // ── Session management (mirrors FINRA's token caching pattern) ──

    private async Task<(string Crumb, string CookieHeader)> EnsureSession() {
        await SessionSemaphore.WaitAsync();
        try {
            if (_cachedCrumb != null && DateTime.UtcNow < _sessionExpiry) {
                return (_cachedCrumb, _cachedCookieHeader);
            }

            _logger.LogDebug("Acquiring Yahoo Finance session (cookie + crumb)");

            var cookieContainer = new CookieContainer();
            using var handler = new HttpClientHandler {
                CookieContainer = cookieContainer,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseCookies = true,
            };
            using var sessionClient = new HttpClient(handler);
            ApplyBrowserHeaders(sessionClient);

            // Step 1: GET fc.yahoo.com to acquire session cookies (response is typically 404, that's expected)
            try {
                await sessionClient.GetAsync(CookieUrl);
            } catch (HttpRequestException) {
                // 404 is expected — we only need the cookies it sets
            }

            // Step 2: GET the crumb endpoint using the cookies from step 1
            var crumbResponse = await sessionClient.GetAsync(CrumbUrl);
            crumbResponse.EnsureSuccessStatusCode();
            var crumb = await crumbResponse.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(crumb)) {
                throw new InvalidOperationException("Yahoo Finance returned an empty crumb");
            }

            // Extract cookies as a header string for use with the DI-injected HttpClient
            var cookieHeader = cookieContainer.GetCookieHeader(new Uri("https://query1.finance.yahoo.com"));

            _cachedCrumb = crumb;
            _cachedCookieHeader = cookieHeader;
            _sessionExpiry = DateTime.UtcNow.AddMinutes(SessionLifetimeMinutes);

            _logger.LogDebug("Yahoo Finance session acquired, expires in {Minutes} minutes", SessionLifetimeMinutes);
            return (crumb, cookieHeader);
        } finally {
            SessionSemaphore.Release();
        }
    }

    private static async Task InvalidateSession() {
        await SessionSemaphore.WaitAsync();
        try {
            _cachedCrumb = null;
            _cachedCookieHeader = null;
            _sessionExpiry = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        } finally {
            SessionSemaphore.Release();
        }
    }

    // ── HTTP with retry (hybrid of FINRA's auth-retry and FRED's simple retry) ──

    private async Task<string> SendWithRetry(string baseUrl) {
        var (crumb, cookieHeader) = await EnsureSession();

        for (var attempt = 0; attempt <= MaxRetries; attempt++) {
            await RateLimiter.WaitAsync();

            var separator = baseUrl.Contains('?') ? "&" : "?";
            var url = $"{baseUrl}{separator}crumb={Uri.EscapeDataString(crumb)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyBrowserHeaders(request);
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

            using var response = await _httpClient.SendAsync(request);

            // Auth failure → refresh session and retry (like FINRA's token refresh on 401)
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) {
                if (attempt >= MaxRetries) break;
                _logger.LogWarning("Yahoo session expired ({StatusCode}), refreshing (attempt {Attempt}/{Max})",
                    (int)response.StatusCode, attempt + 1, MaxRetries);
                await InvalidateSession();
                (crumb, cookieHeader) = await EnsureSession();
                continue;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries) {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("Yahoo rate limited (429), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds, attempt + 1, MaxRetries);
                RateLimiter.PauseFor(delay);
                await Task.Delay(delay);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < MaxRetries) {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("Yahoo server error ({StatusCode}), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    (int)response.StatusCode, delay.TotalSeconds, attempt + 1, MaxRetries);
                RateLimiter.PauseFor(delay);
                await Task.Delay(delay);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        throw new HttpRequestException("Max retries exceeded for Yahoo Finance request");
    }

    // ── Helpers ──

    private static void ApplyBrowserHeaders(HttpClient client) {
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            BrowserUserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/json,*/*");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
    }

    private static void ApplyBrowserHeaders(HttpRequestMessage request) {
        request.Headers.UserAgent.ParseAdd(
            BrowserUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/json,*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
    }

    private static long ToUnixTimestamp(DateOnly date) {
        var dateTime = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return (long)(dateTime - UnixEpoch).TotalSeconds;
    }

    private static DateOnly FromUnixTimestamp(long timestamp) {
        var dateTime = UnixEpoch.AddSeconds(timestamp).UtcDateTime;
        return DateOnly.FromDateTime(dateTime);
    }
}
