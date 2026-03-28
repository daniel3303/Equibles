using System.Text.Json;
using Equibles.Congress.Data.Models;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Congress.HostedService.Models;
using Newtonsoft.Json;
using static Equibles.Congress.HostedService.Services.DisclosureParsingHelper;
using Microsoft.Playwright;
using Equibles.Core.AutoWiring;

namespace Equibles.Congress.HostedService.Services;

[Service]
public class SenateDisclosureClient : IAsyncDisposable {
    private static readonly IRateLimiter RateLimiter = new RateLimiter(maxRequests: 5, timeWindow: TimeSpan.FromSeconds(1));
    private const int MaxRetries = 3;

    private const string BaseUrl = "https://efdsearch.senate.gov";
    private const string HomeUrl = BaseUrl + "/search/home/";
    private const string SearchDataUrl = BaseUrl + "/search/report/data/";
    private const string PtrReportTypeFilter = "[11]"; // Periodic Transaction Report
    private const int BrowserFetchTimeoutMs = 30_000;

    private readonly ILogger<SenateDisclosureClient> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private IPlaywright _playwright;
    private IBrowser _browser;
    private IPage _page;
    private bool _authenticated;

    // JavaScript executed in the browser context to make HTTP requests.
    // Reuses the browser's TLS fingerprint and session cookies to bypass Akamai bot detection.
    // For POST requests, extracts the CSRF token from cookies and includes it in headers and form data.
    private const string BrowserFetchScript = """
        async ({url, formFields}) => {
            const controller = new AbortController();
            const timeoutId = setTimeout(() => controller.abort(), 30000);
            try {
                const options = { signal: controller.signal };
                if (formFields) {
                    const csrfToken = document.cookie.split(';')
                        .map(c => c.trim())
                        .find(c => c.startsWith('csrftoken='))
                        ?.split('=')[1] ?? '';
                    formFields['csrftoken'] = csrfToken;
                    options.method = 'POST';
                    options.headers = {
                        'Content-Type': 'application/x-www-form-urlencoded',
                        'X-CSRFToken': csrfToken,
                        'Referer': location.origin + '/search/',
                    };
                    options.body = new URLSearchParams(formFields).toString();
                }
                const resp = await fetch(url, options);
                return { status: resp.status, body: await resp.text() };
            } finally {
                clearTimeout(timeoutId);
            }
        }
        """;

    public SenateDisclosureClient(ILogger<SenateDisclosureClient> logger) {
        _logger = logger;
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

        await _initLock.WaitAsync(ct);
        try {
            if (_authenticated) return;

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

            // Register cancellation to force-close the browser on shutdown
            ct.Register(() => {
                _browser?.CloseAsync().ConfigureAwait(false);
            });

            var context = await _browser.NewContextAsync();
            context.SetDefaultTimeout(BrowserFetchTimeoutMs);
            _page = await context.NewPageAsync();

            _logger.LogDebug("Navigating to Senate eFD via Playwright Firefox");

            var response = await _page.GotoAsync(HomeUrl, new PageGotoOptions {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30_000
            });

            if (response?.Status != 200)
                throw new HttpRequestException($"Senate eFD home page returned HTTP {response?.Status}");

            // Accept the prohibition agreement disclaimer
            var checkbox = _page.Locator("#agree_statement");
            if (await checkbox.IsVisibleAsync()) {
                await checkbox.CheckAsync();
                await _page.Locator("button[type='submit'], input[type='submit']").ClickAsync();
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }

            _authenticated = true;
            _logger.LogDebug("Senate eFD disclaimer accepted via Playwright Firefox");
        } finally {
            _initLock.Release();
        }
    }

    private async Task<List<SenateReport>> SearchPtrReports(DateOnly from, DateOnly to, CancellationToken ct) {
        var reports = new List<SenateReport>();
        var start = 0;
        const int pageSize = 100;

        while (true) {
            ct.ThrowIfCancellationRequested();

            var formFields = new Dictionary<string, string> {
                ["report_types"] = PtrReportTypeFilter,
                ["filer_types"] = "[]",
                ["submitted_start_date"] = $"{from:MM/dd/yyyy} 00:00:00",
                ["submitted_end_date"] = $"{to:MM/dd/yyyy} 23:59:59",
                ["first_name"] = "",
                ["last_name"] = "",
                ["senator_state"] = "",
                ["start"] = start.ToString(),
                ["length"] = pageSize.ToString(),
            };

            var json = await FetchWithRetry(SearchDataUrl, formFields, ct);
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
        var html = await FetchWithRetry(report.ReportUrl, ct: ct);
        return ParseTransactionsFromHtml(html, report.MemberName, CongressPosition.Senator, report.DateSubmitted, _logger);
    }

    /// <summary>
    /// Makes an HTTP request via the Playwright browser's fetch() API with retry logic.
    /// Bypasses Akamai bot detection by reusing the browser's TLS fingerprint and session cookies.
    /// Pass formFields for POST, or null for GET.
    /// </summary>
    private async Task<string> FetchWithRetry(string url, Dictionary<string, string> formFields = null, CancellationToken ct = default) {
        for (var attempt = 0; attempt <= MaxRetries; attempt++) {
            ct.ThrowIfCancellationRequested();
            await RateLimiter.WaitAsync();

            var result = await _page.EvaluateAsync<JsonElement>(BrowserFetchScript, new { url, formFields });
            var status = result.GetProperty("status").GetInt32();
            var body = result.GetProperty("body").GetString();

            var isRetryable = status == 429 || status >= 500;
            if (isRetryable && attempt < MaxRetries) {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("Senate eFD returned {Status} for {Url}, retrying in {Delay}s (attempt {Attempt}/{MaxRetries})",
                    status, url, delay.TotalSeconds, attempt + 1, MaxRetries);

                if (status == 429) RateLimiter.PauseFor(delay);

                await Task.Delay(delay, ct);
                continue;
            }

            if (status is < 200 or >= 300)
                throw new HttpRequestException($"Senate eFD returned HTTP {status} for {url} (after {attempt + 1} attempt(s))");

            return body;
        }

        throw new HttpRequestException($"Max retries ({MaxRetries}) exceeded for Senate eFD request: {url}");
    }

    public async ValueTask DisposeAsync() {
        GC.SuppressFinalize(this);

        if (_page != null) {
            await _page.Context.CloseAsync();
            _page = null;
        }

        if (_browser != null) {
            await _browser.CloseAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
        _initLock.Dispose();
    }

    private record SenateReport(string MemberName, string ReportUrl, DateOnly DateSubmitted);

    private class SenateSearchResponse {
        [JsonProperty("draw")] public int Draw { get; set; }
        [JsonProperty("recordsTotal")] public int RecordsTotal { get; set; }
        [JsonProperty("recordsFiltered")] public int RecordsFiltered { get; set; }
        [JsonProperty("data")] public List<List<string>> Data { get; set; } = [];
        [JsonProperty("result")] public string Result { get; set; }
    }
}
