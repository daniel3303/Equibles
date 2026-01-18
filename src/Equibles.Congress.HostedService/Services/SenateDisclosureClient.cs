using System.Net;
using Equibles.Congress.Data.Models;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Congress.HostedService.Models;
using Newtonsoft.Json;
using static Equibles.Congress.HostedService.Services.DisclosureParsingHelper;

using Equibles.Core.AutoWiring;

namespace Equibles.Congress.HostedService.Services;

[Service]
public class SenateDisclosureClient : IDisposable {
    private static readonly IRateLimiter RateLimiter = new RateLimiter(maxRequests: 5, timeWindow: TimeSpan.FromSeconds(1));
    private const int MaxRetries = 3;

    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;
    private readonly ILogger<SenateDisclosureClient> _logger;
    private bool _authenticated;

    private const string BaseUrl = "https://efdsearch.senate.gov";
    private const string HomeUrl = BaseUrl + "/search/home/";
    private const string SearchDataUrl = BaseUrl + "/search/report/data/";
    private const string PtrReportTypeFilter = "[11]"; // Periodic Transaction Report

    public SenateDisclosureClient(ILogger<SenateDisclosureClient> logger) {
        _logger = logger;
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler {
            CookieContainer = _cookieContainer,
            AllowAutoRedirect = true
        };
        _httpClient = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(60) };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Equibles/1.0 (contact@equibles.com)");
    }

    public async Task<List<DisclosureTransaction>> GetRecentTransactions(DateOnly fromDate, DateOnly toDate, CancellationToken ct) {
        await EnsureAuthenticated(ct);

        var reports = await SearchPtrReports(fromDate, toDate, ct);
        _logger.LogInformation("Found {Count} Senate PTR reports between {From} and {To}", reports.Count, fromDate, toDate);

        var transactions = new List<DisclosureTransaction>();

        foreach (var report in reports) {
            try {
                ct.ThrowIfCancellationRequested();
                var reportTxns = await FetchAndParseReport(report, ct);
                transactions.AddRange(reportTxns);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to parse Senate report for {Member} at {Url}",
                    report.MemberName, report.ReportUrl);
            }
        }

        _logger.LogInformation("Parsed {Count} transactions from {ReportCount} Senate PTR reports",
            transactions.Count, reports.Count);
        return transactions;
    }

    private async Task EnsureAuthenticated(CancellationToken ct) {
        if (_authenticated) return;

        await RateLimiter.WaitAsync();
        using var homeResponse = await _httpClient.GetAsync(HomeUrl, ct);
        homeResponse.EnsureSuccessStatusCode();

        var csrfToken = GetCsrfToken();
        if (string.IsNullOrEmpty(csrfToken))
            throw new InvalidOperationException("Failed to obtain CSRF token from Senate eFD");

        await RateLimiter.WaitAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, HomeUrl) {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                ["prohibition_agreement"] = "1",
                ["csrfmiddlewaretoken"] = csrfToken
            })
        };
        request.Headers.Add("Referer", HomeUrl);
        request.Headers.Add("X-CSRFToken", csrfToken);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        _authenticated = true;
        _logger.LogDebug("Senate eFD disclaimer accepted");
    }

    private string GetCsrfToken() {
        var cookies = _cookieContainer.GetCookies(new Uri(BaseUrl));
        return cookies["csrftoken"]?.Value ?? "";
    }

    private async Task<List<SenateReport>> SearchPtrReports(DateOnly from, DateOnly to, CancellationToken ct) {
        var reports = new List<SenateReport>();
        var start = 0;
        const int pageSize = 100;

        while (true) {
            ct.ThrowIfCancellationRequested();
            await RateLimiter.WaitAsync();

            var csrfToken = GetCsrfToken();
            var request = new HttpRequestMessage(HttpMethod.Post, SearchDataUrl) {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["report_types"] = PtrReportTypeFilter,
                    ["filer_types"] = "[]",
                    ["submitted_start_date"] = $"{from:MM/dd/yyyy} 00:00:00",
                    ["submitted_end_date"] = $"{to:MM/dd/yyyy} 23:59:59",
                    ["first_name"] = "",
                    ["last_name"] = "",
                    ["senator_state"] = "",
                    ["start"] = start.ToString(),
                    ["length"] = pageSize.ToString(),
                    ["csrftoken"] = csrfToken
                })
            };
            request.Headers.Add("Referer", BaseUrl + "/search/");
            request.Headers.Add("X-CSRFToken", csrfToken);

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonConvert.DeserializeObject<SenateSearchResponse>(json);

            if (result?.Data == null || result.Data.Count == 0) break;

            foreach (var row in result.Data) {
                var report = ParseReportRow(row);
                if (report != null) reports.Add(report);
            }

            _logger.LogDebug("Senate PTR search: fetched {Start}-{End} of {Total}",
                start, start + result.Data.Count, result.RecordsTotal);

            start += pageSize;
            if (start >= result.RecordsTotal) break;
        }

        return reports;
    }

    private SenateReport ParseReportRow(List<string> row) {
        if (row.Count < 5) return null;

        var firstName = row[0]?.Trim() ?? "";
        var lastName = row[1]?.Trim() ?? "";
        var memberName = $"{firstName} {lastName}".Trim();
        if (string.IsNullOrEmpty(memberName)) return null;

        var linkHtml = row[3] ?? "";
        var hrefMatch = HrefRegex().Match(linkHtml);
        if (!hrefMatch.Success) return null;

        var reportPath = hrefMatch.Groups[1].Value;
        var reportUrl = reportPath.StartsWith("http") ? reportPath : BaseUrl + reportPath;

        if (!IsValidDisclosureUrl(reportUrl, BaseUrl)) return null;

        // Skip paper filings (scanned PDFs) — only HTML electronic filings are parseable
        if (reportUrl.Contains("/view/paper/", StringComparison.OrdinalIgnoreCase)) return null;

        if (!DateOnly.TryParse(row[4]?.Trim(), out var dateSubmitted)) {
            _logger.LogDebug("Skipping Senate report with unparseable date: {Date}", row[4]);
            return null;
        }

        return new SenateReport(memberName, reportUrl, dateSubmitted);
    }

    private async Task<List<DisclosureTransaction>> FetchAndParseReport(SenateReport report, CancellationToken ct) {
        using var response = await SendWithRetryAsync(report.ReportUrl, ct);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct);
        return ParseTransactionsFromHtml(html, report.MemberName, CongressPosition.Senator, report.DateSubmitted, _logger);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(string url, CancellationToken ct) {
        for (var attempt = 0; attempt <= MaxRetries; attempt++) {
            await RateLimiter.WaitAsync();
            var response = await _httpClient.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests) {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("Senate eFD rate limited (429) for {Url}, retrying in {Delay}s",
                    url, delay.TotalSeconds);
                RateLimiter.PauseFor(delay);

                if (attempt < MaxRetries) {
                    response.Dispose();
                    await Task.Delay(delay, ct);
                    continue;
                }
            }

            if ((int)response.StatusCode >= 500 && attempt < MaxRetries) {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("Senate eFD server error ({StatusCode}) for {Url}, retrying in {Delay}s",
                    (int)response.StatusCode, url, delay.TotalSeconds);
                response.Dispose();
                await Task.Delay(delay, ct);
                continue;
            }

            return response;
        }

        throw new HttpRequestException($"Max retries ({MaxRetries}) exceeded for Senate eFD request: {url}");
    }

    public void Dispose() => _httpClient.Dispose();

    private record SenateReport(string MemberName, string ReportUrl, DateOnly DateSubmitted);

    private class SenateSearchResponse {
        [JsonProperty("draw")] public int Draw { get; set; }
        [JsonProperty("recordsTotal")] public int RecordsTotal { get; set; }
        [JsonProperty("recordsFiltered")] public int RecordsFiltered { get; set; }
        [JsonProperty("data")] public List<List<string>> Data { get; set; } = [];
        [JsonProperty("result")] public string Result { get; set; }
    }
}
