using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Common.Retry;
using Equibles.Integrations.Finra.Configuration;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Integrations.Finra.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Equibles.Integrations.Finra;

[Service(ServiceLifetime.Scoped, typeof(IFinraClient))]
public class FinraClient : IFinraClient
{
    private const string TokenEndpoint =
        "https://ews.fip.finra.org/fip/rest/ews/oauth2/access_token?grant_type=client_credentials";
    private const string ApiBaseUrl = "https://api.finra.org";
    private const int MaxPageSize = 5000;
    private const int MaxRetries = 3;

    // FINRA dataset field name; referenced as a sort/filter key and in the projected field list.
    private const string SettlementDateField = "settlementDate";

    // FINRA API dataset group for OTC market data (short volume and short interest).
    private const string OtcMarketGroup = "OTCMarket";

    // FINRA API dataset group for the OTC/ATS Transparency weekly summary feed.
    // Note this is distinct from OtcMarketGroup ("OTCMarket"): the weekly dataset
    // lives under the lower-cased "otcMarket" group.
    private const string OtcMarketWeeklyGroup = "otcMarket";

    // FINRA weekly OTC/ATS Transparency dataset field name; the Monday partition key.
    private const string WeekStartDateField = "weekStartDate";

    // summaryTypeCode values for per-security weekly aggregates:
    //   ATS_W_SMBL → ATS (dark-pool) volume aggregated by symbol
    //   OTC_W_SMBL → non-ATS OTC volume aggregated by symbol
    private static readonly string[] OffExchangeSummaryTypeCodes = ["ATS_W_SMBL", "OTC_W_SMBL"];

    private static readonly string[] ShortInterestFields =
    [
        SettlementDateField,
        "symbolCode",
        "issueName",
        "currentShortPositionQuantity",
        "previousShortPositionQuantity",
        "changePreviousNumber",
        "averageDailyVolumeQuantity",
        "daysToCoverQuantity",
        "changePercent",
        "marketClassCode",
    ];

    private static readonly IRateLimiter RateLimiter = new Common.RateLimiter.RateLimiter(
        maxRequests: 20,
        timeWindow: TimeSpan.FromSeconds(1)
    );

    private static readonly SemaphoreSlim TokenSemaphore = new(1, 1);
    private static string _cachedToken;
    private static DateTime _tokenExpiry = DateTime.SpecifyKind(
        DateTime.MinValue,
        DateTimeKind.Utc
    );

    private readonly HttpClient _httpClient;
    private readonly ILogger<FinraClient> _logger;
    private readonly FinraOptions _options;

    public FinraClient(
        HttpClient httpClient,
        ILogger<FinraClient> logger,
        IOptions<FinraOptions> options
    )
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(_options.ClientId) && !string.IsNullOrEmpty(_options.ClientSecret);

    public async Task<List<ShortVolumeRecord>> GetDailyShortVolume(DateOnly date)
    {
        var dateStr = FormatDate(date);
        _logger.LogDebug("Fetching daily short volume for {Date}", dateStr);

        var results = new List<ShortVolumeRecord>();
        await PaginateQuery<ShortVolumeRecord>(
            OtcMarketGroup,
            "regShoDaily",
            offset => new
            {
                fields = new[]
                {
                    "tradeReportDate",
                    "securitiesInformationProcessorSymbolIdentifier",
                    "shortParQuantity",
                    "shortExemptParQuantity",
                    "totalParQuantity",
                    "marketCode",
                },
                dateRangeFilters = new[]
                {
                    new
                    {
                        fieldName = "tradeReportDate",
                        startDate = dateStr,
                        endDate = dateStr,
                    },
                },
                limit = MaxPageSize,
                offset,
            },
            results.AddRange
        );

        _logger.LogDebug("Fetched {Count} short volume records for {Date}", results.Count, dateStr);
        return results;
    }

    public Task<List<ShortInterestRecord>> GetShortInterest(DateOnly settlementDate)
    {
        return GetShortInterestCore(settlementDate, null);
    }

    public Task<List<ShortInterestRecord>> GetShortInterest(
        DateOnly settlementDate,
        IReadOnlyList<string> symbols
    )
    {
        return GetShortInterestCore(settlementDate, symbols);
    }

    private async Task<List<ShortInterestRecord>> GetShortInterestCore(
        DateOnly settlementDate,
        IReadOnlyList<string> symbols
    )
    {
        var dateStr = FormatDate(settlementDate);
        _logger.LogDebug(
            "Fetching short interest for settlement date {Date}{Filter}",
            dateStr,
            symbols != null ? $" (filtered to {symbols.Count} symbols)" : ""
        );

        var results = new List<ShortInterestRecord>();
        await PaginateQuery<ShortInterestRecord>(
            OtcMarketGroup,
            "consolidatedShortInterest",
            offset =>
            {
                var query = new Dictionary<string, object>
                {
                    ["fields"] = ShortInterestFields,
                    ["dateRangeFilters"] = new[]
                    {
                        new
                        {
                            fieldName = SettlementDateField,
                            startDate = dateStr,
                            endDate = dateStr,
                        },
                    },
                };
                if (symbols != null)
                {
                    query["domainFilters"] = new[]
                    {
                        new { fieldName = "symbolCode", values = symbols },
                    };
                }
                query["limit"] = MaxPageSize;
                query["offset"] = offset;
                return query;
            },
            results.AddRange
        );

        _logger.LogDebug(
            "Fetched {Count} short interest records for {Date}",
            results.Count,
            dateStr
        );
        return results;
    }

    public async Task<List<DateOnly>> GetShortInterestSettlementDates()
    {
        // /partitions GET returns 403; the /data endpoint exposes the same dates by paging.
        var dates = await CollectSettlementDates(offset => new
        {
            fields = new[] { SettlementDateField },
            limit = MaxPageSize,
            offset,
        });

        _logger.LogDebug("Fetched {Count} distinct settlement dates", dates.Count);
        return dates.OrderBy(d => d).ToList();
    }

    public Task<List<DateOnly>> GetShortInterestSettlementDatesAfter(DateOnly afterDate)
    {
        _logger.LogDebug("Discovering settlement dates after {Date}", afterDate);
        return DiscoverSettlementDatesInRange(
            afterDate.AddDays(1),
            DateOnly.FromDateTime(DateTime.UtcNow)
        );
    }

    public Task<List<DateOnly>> GetShortInterestSettlementDatesBetween(
        DateOnly startDate,
        DateOnly endDate
    )
    {
        _logger.LogDebug(
            "Discovering settlement dates between {Start} and {End}",
            startDate,
            endDate
        );
        return DiscoverSettlementDatesInRange(startDate, endDate);
    }

    // Page the consolidatedShortInterest dataset over a settlement-date window and return
    // the deduped, ascending set of distinct settlement dates it contains. An empty window
    // (startDate after endDate) short-circuits without an API call.
    private async Task<List<DateOnly>> DiscoverSettlementDatesInRange(
        DateOnly startDate,
        DateOnly endDate
    )
    {
        if (startDate > endDate)
            return [];

        var startDateStr = FormatDate(startDate);
        var endDateStr = FormatDate(endDate);

        var dates = await CollectSettlementDates(offset => new
        {
            fields = new[] { SettlementDateField },
            dateRangeFilters = new[]
            {
                new
                {
                    fieldName = SettlementDateField,
                    startDate = startDateStr,
                    endDate = endDateStr,
                },
            },
            limit = MaxPageSize,
            offset,
        });

        _logger.LogDebug(
            "Discovered {Count} settlement dates in {Start}..{End}",
            dates.Count,
            startDate,
            endDate
        );
        return dates.OrderBy(d => d).ToList();
    }

    public async Task<List<OffExchangeWeeklyRecord>> GetWeeklyOffExchangeVolume(
        DateOnly weekStartDate
    )
    {
        var dateStr = FormatDate(weekStartDate);
        _logger.LogDebug("Fetching weekly off-exchange volume for week starting {Date}", dateStr);

        var results = new List<OffExchangeWeeklyRecord>();
        await PaginateQuery<OffExchangeWeeklyRecord>(
            OtcMarketWeeklyGroup,
            "weeklySummary",
            offset => new
            {
                fields = new[]
                {
                    "issueSymbolIdentifier",
                    WeekStartDateField,
                    "totalWeeklyShareQuantity",
                    "totalWeeklyTradeCount",
                    "tierIdentifier",
                    "summaryTypeCode",
                },
                dateRangeFilters = new[]
                {
                    new
                    {
                        fieldName = WeekStartDateField,
                        startDate = dateStr,
                        endDate = dateStr,
                    },
                },
                domainFilters = new[]
                {
                    new { fieldName = "summaryTypeCode", values = OffExchangeSummaryTypeCodes },
                },
                limit = MaxPageSize,
                offset,
            },
            results.AddRange
        );

        _logger.LogDebug(
            "Fetched {Count} weekly off-exchange volume records for week starting {Date}",
            results.Count,
            dateStr
        );
        return results;
    }

    private async Task<HashSet<DateOnly>> CollectSettlementDates(Func<int, object> buildQuery)
    {
        var dates = new HashSet<DateOnly>();
        await PaginateQuery<ShortInterestRecord>(
            OtcMarketGroup,
            "consolidatedShortInterest",
            buildQuery,
            records =>
            {
                foreach (var record in records)
                {
                    if (
                        !string.IsNullOrEmpty(record.SettlementDate)
                        && DateOnly.TryParse(record.SettlementDate, out var date)
                    )
                    {
                        dates.Add(date);
                    }
                }
            }
        );
        return dates;
    }

    private async Task PaginateQuery<T>(
        string group,
        string name,
        Func<int, object> buildQuery,
        Action<List<T>> onPage
    )
    {
        var offset = 0;
        while (true)
        {
            var page = await PostQuery<T>(group, name, buildQuery(offset));
            if (page.Count == 0)
                break;

            onPage(page);
            if (page.Count < MaxPageSize)
                break;

            offset += page.Count;
        }
    }

    private async Task<List<T>> PostQuery<T>(string group, string name, object query)
    {
        var url = $"{ApiBaseUrl}/data/group/{group}/name/{name}";
        var json = JsonConvert.SerializeObject(query);

        var responseContent = await SendWithRetry(async token =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return await _httpClient.SendAsync(request);
        });

        return JsonConvert.DeserializeObject<List<T>>(responseContent) ?? [];
    }

    private async Task<string> SendWithRetry(Func<string, Task<HttpResponseMessage>> sendRequest)
    {
        var token = await GetAccessToken();

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await RateLimiter.WaitAsync();

            using var response = await sendRequest(token);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogDebug("Token expired, refreshing");
                await InvalidateToken();
                token = await GetAccessToken();
                continue;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
            {
                var delay = ExponentialBackoff(attempt);
                _logger.LogWarning(
                    "Rate limited (429), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                );
                await Task.Delay(delay);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < MaxRetries)
            {
                var delay = ExponentialBackoff(attempt);
                _logger.LogWarning(
                    "Server error ({StatusCode}), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    (int)response.StatusCode,
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                );
                await Task.Delay(delay);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        throw new HttpRequestException("Max retries exceeded for FINRA API request");
    }

    // Thin forwarder so existing reflection-based backoff tests still find the method.
    private static TimeSpan ExponentialBackoff(int attempt) => RetryBackoff.Exponential(attempt);

    // FINRA's API expects Gregorian ISO dates; InvariantCulture keeps the format
    // stable on non-Gregorian threads (e.g. ar-SA emits Hijri otherwise).
    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<string> GetAccessToken()
    {
        await TokenSemaphore.WaitAsync();
        try
        {
            if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
            {
                return _cachedToken;
            }

            _logger.LogDebug("Requesting new FINRA access token");

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")
            );

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
        }
        finally
        {
            TokenSemaphore.Release();
        }
    }

    private static async Task InvalidateToken()
    {
        await TokenSemaphore.WaitAsync();
        try
        {
            _cachedToken = null;
            _tokenExpiry = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        }
        finally
        {
            TokenSemaphore.Release();
        }
    }
}
