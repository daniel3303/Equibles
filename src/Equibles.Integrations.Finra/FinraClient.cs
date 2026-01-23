using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Finra.Configuration;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Integrations.Finra.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Equibles.Integrations.Finra;

[Service(ServiceLifetime.Scoped, typeof(IFinraClient))]
public class FinraClient : IFinraClient {
    private const string TokenEndpoint = "https://ews.fip.finra.org/fip/rest/ews/oauth2/access_token?grant_type=client_credentials";
    private const string ApiBaseUrl = "https://api.finra.org";
    private const int MaxPageSize = 5000;
    private const int MaxRetries = 3;

    private static readonly IRateLimiter RateLimiter = new Common.RateLimiter.RateLimiter(
        maxRequests: 20, timeWindow: TimeSpan.FromSeconds(1));

    private static readonly SemaphoreSlim TokenSemaphore = new(1, 1);
    private static string _cachedToken;
    private static DateTime _tokenExpiry = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

    private readonly HttpClient _httpClient;
    private readonly ILogger<FinraClient> _logger;
    private readonly FinraOptions _options;

    public FinraClient(
        HttpClient httpClient,
        ILogger<FinraClient> logger,
        IOptions<FinraOptions> options
    ) {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_options.ClientId) && !string.IsNullOrEmpty(_options.ClientSecret);

    public async Task<List<ShortVolumeRecord>> GetDailyShortVolume(DateOnly date) {
        var dateStr = date.ToString("yyyy-MM-dd");
        _logger.LogDebug("Fetching daily short volume for {Date}", dateStr);

        var results = new List<ShortVolumeRecord>();
        var offset = 0;

        while (true) {
            var query = new {
                fields = new[] {
                    "tradeReportDate",
                    "securitiesInformationProcessorSymbolIdentifier",
                    "shortParQuantity",
                    "shortExemptParQuantity",
                    "totalParQuantity",
                    "marketCode"
                },
                dateRangeFilters = new[] {
                    new {
                        fieldName = "tradeReportDate",
                        startDate = dateStr,
                        endDate = dateStr
                    }
                },
                limit = MaxPageSize,
                offset
            };

            var page = await PostQuery<ShortVolumeRecord>("OTCMarket", "regShoDaily", query);
            if (page.Count == 0) break;

            results.AddRange(page);
            if (page.Count < MaxPageSize) break;

            offset += page.Count;
        }

        _logger.LogDebug("Fetched {Count} short volume records for {Date}", results.Count, dateStr);
        return results;
    }

    public async Task<List<ShortInterestRecord>> GetShortInterest(DateOnly settlementDate) {
        var dateStr = settlementDate.ToString("yyyy-MM-dd");
        _logger.LogDebug("Fetching short interest for settlement date {Date}", dateStr);

        var results = new List<ShortInterestRecord>();
        var offset = 0;

        while (true) {
            var query = new {
                fields = new[] {
                    "settlementDate",
                    "symbolCode",
                    "issueName",
                    "currentShortPositionQuantity",
                    "previousShortPositionQuantity",
                    "changePreviousNumber",
                    "averageDailyVolumeQuantity",
                    "daysToCoverQuantity",
                    "changePercent",
                    "marketClassCode"
                },
                dateRangeFilters = new[] {
                    new {
                        fieldName = "settlementDate",
                        startDate = dateStr,
                        endDate = dateStr
                    }
                },
                limit = MaxPageSize,
                offset
            };

            var page = await PostQuery<ShortInterestRecord>("OTCMarket", "consolidatedShortInterest", query);
            if (page.Count == 0) break;

            results.AddRange(page);
            if (page.Count < MaxPageSize) break;

            offset += page.Count;
        }

        _logger.LogDebug("Fetched {Count} short interest records for {Date}", results.Count, dateStr);
        return results;
    }

    public async Task<List<DateOnly>> GetShortInterestSettlementDates() {
        // Query distinct settlement dates via the data endpoint (the /partitions GET returns 403).
        // Sort descending and extract unique dates across pages.
        var dates = new HashSet<DateOnly>();
        var offset = 0;

        while (true) {
            var query = new {
                fields = new[] { "settlementDate" },
                limit = MaxPageSize,
                offset
            };

            var records = await PostQuery<ShortInterestRecord>("OTCMarket", "consolidatedShortInterest", query);
            if (records.Count == 0) break;

            foreach (var record in records) {
                if (!string.IsNullOrEmpty(record.SettlementDate)
                    && DateOnly.TryParse(record.SettlementDate, out var date)) {
                    dates.Add(date);
                }
            }

            if (records.Count < MaxPageSize) break;
            offset += records.Count;
        }

        _logger.LogDebug("Fetched {Count} distinct settlement dates", dates.Count);
        return dates.OrderBy(d => d).ToList();
    }

    private async Task<List<T>> PostQuery<T>(string group, string name, object query) {
        var url = $"{ApiBaseUrl}/data/group/{group}/name/{name}";
        var json = JsonConvert.SerializeObject(query);

        var responseContent = await SendWithRetry(async token => {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return await _httpClient.SendAsync(request);
        });

        return JsonConvert.DeserializeObject<List<T>>(responseContent) ?? [];
    }

    private async Task<string> SendWithRetry(Func<string, Task<HttpResponseMessage>> sendRequest) {
        var token = await GetAccessToken();

        for (var attempt = 0; attempt <= MaxRetries; attempt++) {
            await RateLimiter.WaitAsync();

            using var response = await sendRequest(token);

            if (response.StatusCode == HttpStatusCode.Unauthorized) {
                _logger.LogDebug("Token expired, refreshing");
                await InvalidateToken();
                token = await GetAccessToken();
                continue;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries) {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("Rate limited (429), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds, attempt + 1, MaxRetries);
                await Task.Delay(delay);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < MaxRetries) {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("Server error ({StatusCode}), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    (int)response.StatusCode, delay.TotalSeconds, attempt + 1, MaxRetries);
                await Task.Delay(delay);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        throw new HttpRequestException("Max retries exceeded for FINRA API request");
    }

    private async Task<string> GetAccessToken() {
        await TokenSemaphore.WaitAsync();
        try {
            if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry) {
                return _cachedToken;
            }

            _logger.LogDebug("Requesting new FINRA access token");

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonConvert.DeserializeObject<FinraTokenResponse>(content);

            var accessToken = tokenResponse.AccessToken;
            var expiresIn = tokenResponse.ExpiresIn;

            _cachedToken = accessToken;
            // Refresh 60 seconds before actual expiry
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);

            _logger.LogDebug("FINRA access token acquired, expires in {ExpiresIn}s", expiresIn);
            return accessToken;
        } finally {
            TokenSemaphore.Release();
        }
    }

    private static async Task InvalidateToken() {
        await TokenSemaphore.WaitAsync();
        try {
            _cachedToken = null;
            _tokenExpiry = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        } finally {
            TokenSemaphore.Release();
        }
    }
}
