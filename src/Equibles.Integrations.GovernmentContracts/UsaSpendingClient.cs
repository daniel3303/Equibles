using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Common.Retry;
using Equibles.Integrations.GovernmentContracts.Contracts;
using Equibles.Integrations.GovernmentContracts.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Equibles.Integrations.GovernmentContracts;

[Service(ServiceLifetime.Scoped, typeof(IUsaSpendingClient))]
public class UsaSpendingClient : IUsaSpendingClient
{
    private const string SearchUrl = "https://api.usaspending.gov/api/v2/search/spending_by_award/";
    private const int MaxRetries = 3;

    // The API returns at most 100 rows/page and refuses to paginate past the
    // 10,000th record, so 100 pages is the hard ceiling for a single window.
    private const int PageSize = 100;
    private const int MaxPages = 100;

    // Federal procurement contract award types (excludes grants/loans/other assistance).
    private static readonly string[] ContractAwardTypeCodes = ["A", "B", "C", "D"];

    private static readonly string[] RequestFields =
    [
        "Award ID",
        "Recipient Name",
        "recipient_id",
        "Award Amount",
        "Total Outlays",
        "Awarding Agency",
        "Contract Award Type",
        "Start Date",
        "End Date",
        "Last Modified Date",
        "NAICS",
        "PSC",
        "Description",
    ];

    private static readonly IRateLimiter RateLimiter = new Common.RateLimiter.RateLimiter(
        maxRequests: 10,
        timeWindow: TimeSpan.FromSeconds(1)
    );

    private readonly HttpClient _httpClient;
    private readonly ILogger<UsaSpendingClient> _logger;

    public UsaSpendingClient(HttpClient httpClient, ILogger<UsaSpendingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<UsaSpendingAwardRecord>> GetContractAwards(
        DateOnly startDate,
        DateOnly endDate,
        decimal minimumAmount
    )
    {
        var startStr = FormatDate(startDate);
        var endStr = FormatDate(endDate);
        var results = new List<UsaSpendingAwardRecord>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var body = BuildRequestBody(startStr, endStr, minimumAmount, page);
            var response = await PostQuery(body);
            if (response.Results.Count > 0)
            {
                results.AddRange(response.Results);
            }

            if (response.PageMetadata is not { HasNext: true })
            {
                return results;
            }

            if (page == MaxPages)
            {
                _logger.LogWarning(
                    "USAspending window {Start}..{End} (>= ${Min}) exceeded {MaxPages} pages "
                        + "({Count} awards fetched) and has more results — narrow the window or raise the floor",
                    startStr,
                    endStr,
                    minimumAmount,
                    MaxPages,
                    results.Count
                );
            }
        }

        return results;
    }

    private static object BuildRequestBody(
        string startDate,
        string endDate,
        decimal minimumAmount,
        int page
    )
    {
        return new
        {
            filters = new
            {
                award_type_codes = ContractAwardTypeCodes,
                time_period = new[] { new { start_date = startDate, end_date = endDate } },
                award_amounts = new[] { new { lower_bound = minimumAmount } },
            },
            fields = RequestFields,
            page,
            limit = PageSize,
            sort = "Award Amount",
            order = "desc",
            subawards = false,
        };
    }

    private async Task<UsaSpendingAwardResponse> PostQuery(object body)
    {
        var json = JsonConvert.SerializeObject(body);
        var content = await SendWithRetry(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, SearchUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return _httpClient.SendAsync(request);
        });

        var response =
            JsonConvert.DeserializeObject<UsaSpendingAwardResponse>(content)
            ?? new UsaSpendingAwardResponse();
        // A page with "results": null overwrites the model's default empty list with null;
        // restore the non-null invariant so callers can read Results without a guard.
        response.Results ??= [];
        return response;
    }

    private async Task<string> SendWithRetry(Func<Task<HttpResponseMessage>> sendRequest)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await RateLimiter.WaitAsync();

            HttpResponseMessage response;
            try
            {
                response = await sendRequest();
            }
            catch (Exception ex)
                when (ex is HttpRequestException or TaskCanceledException && attempt < MaxRetries)
            {
                // A transport-level failure — connection reset, DNS blip, TLS error or a request
                // timeout — throws here and never reaches the status-code checks below. Without this
                // a momentary network hiccup fails the whole import window (and, repeated across every
                // window, floods the error log); retry it with the same backoff a 5xx gets.
                var delay = ExponentialBackoff(attempt);
                _logger.LogWarning(
                    ex,
                    "USAspending request failed ({Error}), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    ex.Message,
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                );
                await Task.Delay(delay);
                continue;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
                {
                    var delay = ExponentialBackoff(attempt);
                    _logger.LogWarning(
                        "USAspending rate limited (429), retrying in {Delay}s (attempt {Attempt}/{Max})",
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
                        "USAspending server error ({StatusCode}), retrying in {Delay}s (attempt {Attempt}/{Max})",
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
        }

        throw new HttpRequestException("Max retries exceeded for USAspending API request");
    }

    // Thin forwarder so reflection-based backoff tests can find the method.
    private static TimeSpan ExponentialBackoff(int attempt) => RetryBackoff.Exponential(attempt);

    // USAspending expects Gregorian ISO dates; InvariantCulture keeps the format
    // stable on non-Gregorian threads.
    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
