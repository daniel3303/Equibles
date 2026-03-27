using System.Net;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Fred.Configuration;
using Equibles.Integrations.Fred.Contracts;
using Equibles.Integrations.Fred.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Equibles.Integrations.Fred;

[Service(ServiceLifetime.Scoped, typeof(IFredClient))]
public class FredClient : IFredClient {
    private const string ApiBaseUrl = "https://api.stlouisfed.org";
    private const int MaxRetries = 3;
    private const int MaxObservationsPerRequest = 100000;

    // FRED allows 120 requests/minute — use 100 to stay safely under
    private static readonly IRateLimiter RateLimiter = new Common.RateLimiter.RateLimiter(
        maxRequests: 100, timeWindow: TimeSpan.FromMinutes(1));

    private readonly HttpClient _httpClient;
    private readonly ILogger<FredClient> _logger;
    private readonly FredOptions _options;

    public FredClient(
        HttpClient httpClient,
        ILogger<FredClient> logger,
        IOptions<FredOptions> options
    ) {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_options.ApiKey);

    public async Task<FredSeriesRecord> GetSeriesMetadata(string seriesId) {
        _logger.LogDebug("Fetching FRED series metadata for {SeriesId}", seriesId);

        // FRED API only supports API key via query parameter (no header-based auth available).
        // HttpClient request logging should be suppressed to avoid exposing the key in logs.
        var url = $"{ApiBaseUrl}/fred/series?series_id={seriesId}&api_key={_options.ApiKey}&file_type=json";
        var content = await SendWithRetry(url);
        var response = JsonConvert.DeserializeObject<FredSeriesResponse>(content);

        return response?.Series?.FirstOrDefault();
    }

    public async Task<List<FredObservationRecord>> GetObservations(string seriesId, DateOnly? startDate = null) {
        _logger.LogDebug("Fetching FRED observations for {SeriesId} from {StartDate}", seriesId, startDate);

        var allObservations = new List<FredObservationRecord>();
        var offset = 0;

        while (true) {
            var url = $"{ApiBaseUrl}/fred/series/observations" +
                      $"?series_id={seriesId}" +
                      $"&api_key={_options.ApiKey}" +
                      $"&file_type=json" +
                      $"&sort_order=asc" +
                      $"&limit={MaxObservationsPerRequest}" +
                      $"&offset={offset}";

            if (startDate.HasValue) {
                url += $"&observation_start={startDate.Value:yyyy-MM-dd}";
            }

            var content = await SendWithRetry(url);
            var response = JsonConvert.DeserializeObject<FredObservationsResponse>(content);

            if (response?.Observations == null || response.Observations.Count == 0) break;

            allObservations.AddRange(response.Observations);

            if (allObservations.Count >= response.Count) break;
            offset += response.Observations.Count;
        }

        _logger.LogDebug("Fetched {Count} observations for {SeriesId}", allObservations.Count, seriesId);
        return allObservations;
    }

    private async Task<string> SendWithRetry(string url) {
        for (var attempt = 0; attempt <= MaxRetries; attempt++) {
            await RateLimiter.WaitAsync();

            using var response = await _httpClient.GetAsync(url);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries) {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("FRED rate limited (429), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds, attempt + 1, MaxRetries);
                RateLimiter.PauseFor(delay);
                await Task.Delay(delay);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < MaxRetries) {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("FRED server error ({StatusCode}), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    (int)response.StatusCode, delay.TotalSeconds, attempt + 1, MaxRetries);
                await Task.Delay(delay);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        throw new HttpRequestException("Max retries exceeded for FRED API request");
    }
}
