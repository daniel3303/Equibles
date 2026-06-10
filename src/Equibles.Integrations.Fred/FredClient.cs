using Equibles.Core.AutoWiring;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Common.Retry;
using Equibles.Integrations.Fred.Configuration;
using Equibles.Integrations.Fred.Contracts;
using Equibles.Integrations.Fred.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Equibles.Integrations.Fred;

[Service(ServiceLifetime.Scoped, typeof(IFredClient))]
public class FredClient : IFredClient
{
    private const string ApiBaseUrl = "https://api.stlouisfed.org";
    private const int MaxRetries = 3;
    private const int MaxObservationsPerRequest = 100000;

    // The /fred/releases/dates endpoint caps its page size at 1000.
    private const int MaxReleaseDatesPerRequest = 1000;

    // FRED allows 120 requests/minute — use 100 to stay safely under
    private static readonly IRateLimiter RateLimiter = new Common.RateLimiter.RateLimiter(
        maxRequests: 100,
        timeWindow: TimeSpan.FromMinutes(1)
    );

    private readonly HttpClient _httpClient;
    private readonly ILogger<FredClient> _logger;
    private readonly FredOptions _options;

    public FredClient(
        HttpClient httpClient,
        ILogger<FredClient> logger,
        IOptions<FredOptions> options
    )
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_options.ApiKey);

    public async Task<FredSeriesRecord> GetSeriesMetadata(string seriesId)
    {
        _logger.LogDebug("Fetching FRED series metadata for {SeriesId}", seriesId);

        // FRED API only supports API key via query parameter (no header-based auth available).
        // HttpClient request logging should be suppressed to avoid exposing the key in logs.
        var url =
            $"{ApiBaseUrl}/fred/series?series_id={seriesId}&api_key={_options.ApiKey}&file_type=json";
        var content = await SendWithRetry(url);
        var response = JsonConvert.DeserializeObject<FredSeriesResponse>(content);

        return response?.Series?.FirstOrDefault();
    }

    public async Task<List<FredObservationRecord>> GetObservations(
        string seriesId,
        DateOnly? startDate = null
    )
    {
        _logger.LogDebug(
            "Fetching FRED observations for {SeriesId} from {StartDate}",
            seriesId,
            startDate
        );

        var allObservations = new List<FredObservationRecord>();
        var offset = 0;

        while (true)
        {
            var url =
                $"{ApiBaseUrl}/fred/series/observations"
                + $"?series_id={seriesId}"
                + $"&api_key={_options.ApiKey}"
                + $"&file_type=json"
                + $"&sort_order=asc"
                + $"&limit={MaxObservationsPerRequest}"
                + $"&offset={offset}";

            if (startDate.HasValue)
            {
                url += $"&observation_start={startDate.Value:yyyy-MM-dd}";
            }

            var content = await SendWithRetry(url);
            var response = JsonConvert.DeserializeObject<FredObservationsResponse>(content);

            if (response?.Observations == null || response.Observations.Count == 0)
                break;

            allObservations.AddRange(response.Observations);

            if (allObservations.Count >= response.Count)
                break;
            offset += response.Observations.Count;
        }

        _logger.LogDebug(
            "Fetched {Count} observations for {SeriesId}",
            allObservations.Count,
            seriesId
        );
        return allObservations;
    }

    public async Task<FredReleaseRecord> GetSeriesRelease(string seriesId)
    {
        _logger.LogDebug("Fetching FRED release for series {SeriesId}", seriesId);

        var url =
            $"{ApiBaseUrl}/fred/series/release?series_id={seriesId}&api_key={_options.ApiKey}&file_type=json";
        var content = await SendWithRetry(url);
        var response = JsonConvert.DeserializeObject<FredReleasesResponse>(content);

        return response?.Releases?.FirstOrDefault();
    }

    public async Task<List<FredReleaseDateRecord>> GetReleaseDates(DateOnly? realtimeStart = null)
    {
        _logger.LogDebug("Fetching FRED release dates from {RealtimeStart}", realtimeStart);

        var allReleaseDates = new List<FredReleaseDateRecord>();
        var offset = 0;

        while (true)
        {
            // include_release_dates_with_no_data=true is what surfaces future scheduled
            // dates from the FRED release calendar — without it only realized dates return.
            var url =
                $"{ApiBaseUrl}/fred/releases/dates"
                + $"?api_key={_options.ApiKey}"
                + $"&file_type=json"
                + $"&include_release_dates_with_no_data=true"
                + $"&sort_order=asc"
                + $"&limit={MaxReleaseDatesPerRequest}"
                + $"&offset={offset}";

            if (realtimeStart.HasValue)
            {
                url += $"&realtime_start={realtimeStart.Value:yyyy-MM-dd}";
            }

            var content = await SendWithRetry(url);
            var response = JsonConvert.DeserializeObject<FredReleasesDatesResponse>(content);

            if (response?.ReleaseDates == null || response.ReleaseDates.Count == 0)
                break;

            allReleaseDates.AddRange(response.ReleaseDates);

            if (allReleaseDates.Count >= response.Count)
                break;
            offset += response.ReleaseDates.Count;
        }

        _logger.LogDebug("Fetched {Count} FRED release dates", allReleaseDates.Count);
        return allReleaseDates;
    }

    private async Task<string> SendWithRetry(string url)
    {
        using var response = await HttpRetry.Send(
            () => _httpClient.GetAsync(url),
            RateLimiter,
            MaxRetries,
            "Max retries exceeded for FRED API request",
            (attempt, delay) =>
                _logger.LogWarning(
                    "FRED rate limited (429), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                ),
            (statusCode, attempt, delay) =>
                _logger.LogWarning(
                    "FRED server error ({StatusCode}), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    statusCode,
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                )
        );
        return await response.Content.ReadAsStringAsync();
    }

    // Thin forwarder so existing reflection-based backoff tests still find the method.
    private static TimeSpan ExponentialBackoff(int attempt) => RetryBackoff.Exponential(attempt);
}
