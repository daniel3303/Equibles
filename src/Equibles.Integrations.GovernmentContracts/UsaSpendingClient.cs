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

    // USAspending's search endpoint has intermittent bad spells lasting minutes —
    // the gateway resets heavy queries mid-flight, which surfaces as an
    // HttpRequestException on an otherwise healthy API. Three retries rode ~30s of
    // backoff (2+4+8+16s) and kept losing whole import windows to spells barely
    // longer than that; six retries back off ~2min (…+32+64s), long enough to
    // outlast the spell while a genuine outage still fails the window promptly.
    private const int MaxRetries = 6;

    // The API returns at most 100 rows/page.
    private const int PageSize = 100;

    // Offset pagination degrades sharply with depth (measured against the live API:
    // page 1 ≈ 1.5s, page 25 ≈ 41s, page 50 ≈ 55s, page 90 drops the connection
    // mid-body — a deterministic "response ended prematurely", not a blip). Results
    // are sorted by award amount descending, so instead of paging deep the client
    // pages at most this many pages, then tightens the band's upper amount bound to
    // the smallest amount fetched and restarts from page 1 — every request stays
    // shallow. A fresh band costs one slow query (~45–60s while the server builds
    // its filter cache) and then ~1–2s per page, well inside the request timeout.
    private const int MaxPagesPerBand = 10;

    // Hard per-band ceiling — the API refuses to paginate past the 10,000th record.
    // The cursor only pages past MaxPagesPerBand while stuck on a run of awards tied
    // at the exact same amount (the upper bound can't tighten), so reaching this
    // means 10,000+ same-amount awards in the window; FetchWindow then bisects the
    // date range so the tie run lands in smaller windows.
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
        "Base Obligation Date",
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
        decimal minimumAmount,
        CancellationToken cancellationToken = default
    )
    {
        var results = new List<UsaSpendingAwardRecord>();
        // Amount-band resets re-fetch awards tied at the boundary amount (the upper
        // bound is inclusive), so duplicates are dropped here by the award's
        // globally-unique id rather than surfacing to callers.
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        await FetchWindow(
            startDate,
            endDate,
            minimumAmount,
            initialUpperBound: null,
            results,
            seenIds,
            cancellationToken
        );
        return results;
    }

    /// <summary>
    /// Fetches every award in the window with amount in [<paramref name="minimumAmount"/>,
    /// <paramref name="initialUpperBound"/>] using an amount-descending cursor: page at most
    /// <see cref="MaxPagesPerBand"/> shallow pages, then restart from page 1 with the upper
    /// bound tightened to the smallest amount fetched. Completeness holds by induction —
    /// results sort by amount descending, so everything above the new bound is already
    /// fetched, and the inclusive bound re-covers ties at the boundary (deduplicated by id).
    /// The cursor only pages deeper while stalled on a same-amount tie run; a run too long
    /// even for that (<see cref="MaxPages"/>) bisects the date range instead.
    /// </summary>
    private async Task FetchWindow(
        DateOnly startDate,
        DateOnly endDate,
        decimal minimumAmount,
        decimal? initialUpperBound,
        List<UsaSpendingAwardRecord> results,
        HashSet<string> seenIds,
        CancellationToken cancellationToken
    )
    {
        var startStr = FormatDate(startDate);
        var endStr = FormatDate(endDate);
        var upperBound = initialUpperBound;

        for (var page = 1; ; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var body = BuildRequestBody(startStr, endStr, minimumAmount, upperBound, page);
            var response = await PostQuery(body, cancellationToken);

            foreach (var record in response.Results)
            {
                if (record.GeneratedInternalId == null || seenIds.Add(record.GeneratedInternalId))
                {
                    results.Add(record);
                }
            }

            if (response.PageMetadata is not { HasNext: true })
            {
                return;
            }

            if (response.Results.Count == 0)
            {
                // hasNext on an empty page is a server contradiction; stop rather
                // than loop on it forever.
                _logger.LogWarning(
                    "USAspending window {Start}..{End} returned an empty page {Page} with hasNext=true; stopping",
                    startStr,
                    endStr,
                    page
                );
                return;
            }

            // Sorted by amount descending, so the last row carries the smallest
            // amount fetched so far. The API's award_amounts validator only accepts
            // whole-dollar values — any fractional bound is a 422 ("'x.51' is not a
            // valid type (dictionary)") — so the cursor's next inclusive upper bound
            // is the floor rounded UP to a whole dollar: everything between the floor
            // and its ceiling is re-covered by the next band and deduplicated, so
            // nothing is lost. The reset requires the CEILED value to strictly
            // decrease — a run of awards within the same dollar rides the tie-run
            // continuation below instead of looping on an unchanged bound.
            var floor = response.Results[^1].Amount;
            var nextUpperBound = floor.HasValue ? decimal.Ceiling(floor.Value) : (decimal?)null;

            if (
                page >= MaxPagesPerBand
                && nextUpperBound.HasValue
                && nextUpperBound.Value < (upperBound ?? decimal.MaxValue)
            )
            {
                upperBound = nextUpperBound.Value;
                page = 0; // restart the band shallow; the for-loop increments to 1
                continue;
            }

            if (page >= MaxPages)
            {
                if (startDate < endDate)
                {
                    // 10,000+ awards tied at one amount (or amount-less rows) in this
                    // window — the cursor can't advance, so split the dates and let each
                    // half re-cover the tie run; overlap is deduplicated by award id.
                    var mid = startDate.AddDays((endDate.DayNumber - startDate.DayNumber) / 2);
                    _logger.LogInformation(
                        "USAspending window {Start}..{End} has an unpageable tie run at <= {Upper}; "
                            + "bisecting at {Mid}",
                        startStr,
                        endStr,
                        upperBound,
                        FormatDate(mid)
                    );
                    await FetchWindow(
                        startDate,
                        mid,
                        minimumAmount,
                        upperBound,
                        results,
                        seenIds,
                        cancellationToken
                    );
                    await FetchWindow(
                        mid.AddDays(1),
                        endDate,
                        minimumAmount,
                        upperBound,
                        results,
                        seenIds,
                        cancellationToken
                    );
                    return;
                }

                // A single day with 10,000+ awards at the exact same amount cannot be
                // enumerated through this endpoint at all; log the truncation loudly.
                _logger.LogWarning(
                    "USAspending single-day window {Start} (>= ${Min}) still has results after "
                        + "{MaxPages} pages tied at <= {Upper} ({Count} awards fetched so far); "
                        + "remaining ties are unreachable through this endpoint",
                    startStr,
                    minimumAmount,
                    MaxPages,
                    upperBound,
                    results.Count
                );
                return;
            }
        }
    }

    private static object BuildRequestBody(
        string startDate,
        string endDate,
        decimal minimumAmount,
        decimal? maximumAmount,
        int page
    )
    {
        // Both amount bounds are INCLUSIVE (verified against the live API: the counts of
        // [a,b] and [b,∞) overlap by exactly the count of [b,b]) — the amount cursor
        // relies on that to re-cover awards tied at the boundary instead of losing them.
        object[] awardAmounts = maximumAmount.HasValue
            ? [new { lower_bound = minimumAmount, upper_bound = maximumAmount.Value }]
            : [new { lower_bound = minimumAmount }];

        return new
        {
            filters = new
            {
                award_type_codes = ContractAwardTypeCodes,
                time_period = new[] { new { start_date = startDate, end_date = endDate } },
                award_amounts = awardAmounts,
            },
            fields = RequestFields,
            page,
            limit = PageSize,
            sort = "Award Amount",
            order = "desc",
            subawards = false,
        };
    }

    private async Task<UsaSpendingAwardResponse> PostQuery(
        object body,
        CancellationToken cancellationToken
    )
    {
        var json = JsonConvert.SerializeObject(body);
        var content = await SendWithRetry(
            async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, SearchUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                return await _httpClient.SendAsync(request, cancellationToken);
            },
            cancellationToken
        );

        var response =
            JsonConvert.DeserializeObject<UsaSpendingAwardResponse>(content)
            ?? new UsaSpendingAwardResponse();
        // A page with "results": null overwrites the model's default empty list with null;
        // restore the non-null invariant so callers can read Results without a guard.
        response.Results ??= [];
        return response;
    }

    private async Task<string> SendWithRetry(
        Func<Task<HttpResponseMessage>> sendRequest,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await RateLimiter.WaitAsync(cancellationToken);

            HttpResponseMessage response;
            try
            {
                response = await sendRequest();
            }
            catch (Exception ex)
                when (ex is HttpRequestException or TaskCanceledException
                    && attempt < MaxRetries
                    && !cancellationToken.IsCancellationRequested
                )
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
                await Task.Delay(delay, cancellationToken);
                continue;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Only reachable once the retry filter above stops matching, i.e. on the
                // final attempt: an HttpClient timeout (not our token) surfaces as
                // TaskCanceledException, which the import loop and the worker's report
                // gate both treat as shutdown — a persistent-timeout outage would back
                // off silently forever and never reach the Errors page. Map it to the
                // transport failure it actually is.
                throw new HttpRequestException(
                    "USAspending request timed out after exhausting retries",
                    ex
                );
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
                    await Task.Delay(delay, cancellationToken);
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
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken);
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
