using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Common.RateLimiter;
using Newtonsoft.Json;
using static Equibles.Congress.HostedService.Services.DisclosureParsingHelper;

namespace Equibles.Congress.HostedService.Services;

[Service]
public class SenateDisclosureClient : IAsyncDisposable
{
    private static readonly IRateLimiter RateLimiter = new RateLimiter(
        maxRequests: 5,
        timeWindow: TimeSpan.FromSeconds(1)
    );
    private const int MaxRetries = 3;

    private const string BaseUrl = "https://efdsearch.senate.gov";
    private const string SearchDataUrl = BaseUrl + "/search/report/data/";
    private const string PtrReportTypeFilter = "[11]"; // Periodic Transaction Report

    private readonly ISenateBrowserSession _session;
    private readonly ILogger<SenateDisclosureClient> _logger;

    public SenateDisclosureClient(
        ISenateBrowserSession session,
        ILogger<SenateDisclosureClient> logger
    )
    {
        _session = session;
        _logger = logger;
    }

    public async Task<List<DisclosureTransaction>> GetRecentTransactions(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct
    )
    {
        await _session.EnsureAuthenticated(ct);

        var reports = await SearchPtrReports(fromDate, toDate, ct);
        _logger.LogInformation(
            "Found {Count} Senate PTR reports between {From} and {To}",
            reports.Count,
            fromDate,
            toDate
        );

        var transactions = new List<DisclosureTransaction>();

        foreach (var report in reports)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var reportTxns = await FetchAndParseReport(report, ct);
                transactions.AddRange(reportTxns);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to parse Senate report for {Member} at {Url}",
                    report.MemberName,
                    report.ReportUrl
                );
            }
        }

        _logger.LogInformation(
            "Parsed {Count} transactions from {ReportCount} Senate PTR reports",
            transactions.Count,
            reports.Count
        );
        return transactions;
    }

    private async Task<List<SenateReport>> SearchPtrReports(
        DateOnly from,
        DateOnly to,
        CancellationToken ct
    )
    {
        var reports = new List<SenateReport>();
        var start = 0;
        const int pageSize = 100;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var formFields = new Dictionary<string, string>
            {
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

            if (result?.Data == null || result.Data.Count == 0)
                break;

            foreach (var row in result.Data)
            {
                var report = ParseReportRow(row);
                if (report != null)
                    reports.Add(report);
            }

            _logger.LogDebug(
                "Senate PTR search: fetched {Start}-{End} of {Total}",
                start,
                start + result.Data.Count,
                result.RecordsTotal
            );

            start += pageSize;
            if (start >= result.RecordsTotal)
                break;
        }

        return reports;
    }

    private SenateReport ParseReportRow(List<string> row)
    {
        if (row.Count < 5)
            return null;

        var firstName = row[0]?.Trim() ?? "";
        var lastName = row[1]?.Trim() ?? "";
        var memberName = $"{firstName} {lastName}".Trim();
        if (string.IsNullOrEmpty(memberName))
            return null;

        var linkHtml = row[3] ?? "";
        var hrefMatch = HrefRegex().Match(linkHtml);
        if (!hrefMatch.Success)
            return null;

        var reportPath = hrefMatch.Groups[1].Value;
        var reportUrl = reportPath.StartsWith("http") ? reportPath : BaseUrl + reportPath;

        if (!IsValidDisclosureUrl(reportUrl, BaseUrl))
            return null;

        // Skip paper filings (scanned PDFs) — only HTML electronic filings are parseable
        if (reportUrl.Contains("/view/paper/", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!DateOnly.TryParse(row[4]?.Trim(), out var dateSubmitted))
        {
            _logger.LogDebug("Skipping Senate report with unparseable date: {Date}", row[4]);
            return null;
        }

        return new SenateReport(memberName, reportUrl, dateSubmitted);
    }

    private async Task<List<DisclosureTransaction>> FetchAndParseReport(
        SenateReport report,
        CancellationToken ct
    )
    {
        var html = await FetchWithRetry(report.ReportUrl, ct: ct);
        return ParseTransactionsFromHtml(
            html,
            report.MemberName,
            CongressPosition.Senator,
            report.DateSubmitted,
            _logger
        );
    }

    /// <summary>
    /// Issues a request through the browser session with retry logic. Pass
    /// formFields for POST, or null for GET.
    /// </summary>
    private async Task<string> FetchWithRetry(
        string url,
        Dictionary<string, string> formFields = null,
        CancellationToken ct = default
    )
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await RateLimiter.WaitAsync();

            SenateFetchResult result;
            try
            {
                result = await _session.Fetch(url, formFields, ct);
            }
            catch (SenateBrowserException ex)
            {
                if (attempt >= MaxRetries)
                {
                    _logger.LogError(
                        ex,
                        "Browser fetch failed for {Url} after {Attempts} attempt(s)",
                        url,
                        attempt + 1
                    );
                    throw;
                }
                var delay = ExponentialBackoff(attempt);
                _logger.LogWarning(
                    "Browser fetch error for {Url}, retrying in {Delay}s (attempt {Attempt}/{MaxRetries}): {Message}",
                    url,
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries,
                    ex.Message
                );
                await Task.Delay(delay, ct);
                continue;
            }

            var status = result.Status;
            var body = result.Body;

            var isRetryable = status == 429 || status >= 500;
            if (isRetryable && attempt < MaxRetries)
            {
                var delay = ExponentialBackoff(attempt);
                _logger.LogWarning(
                    "Senate eFD returned {Status} for {Url}, retrying in {Delay}s (attempt {Attempt}/{MaxRetries})",
                    status,
                    url,
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                );

                if (status == 429)
                    RateLimiter.PauseFor(delay);

                await Task.Delay(delay, ct);
                continue;
            }

            if (status is < 200 or >= 300)
            {
                _logger.LogError(
                    "Senate eFD returned HTTP {Status} for {Url} after {Attempts} attempt(s)",
                    status,
                    url,
                    attempt + 1
                );
                throw new HttpRequestException(
                    $"Senate eFD returned HTTP {status} for {url} (after {attempt + 1} attempt(s))"
                );
            }

            return body;
        }

        throw new HttpRequestException(
            $"Max retries ({MaxRetries}) exceeded for Senate eFD request: {url}"
        );
    }

    private static TimeSpan ExponentialBackoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await _session.DisposeAsync();
    }

    private record SenateReport(string MemberName, string ReportUrl, DateOnly DateSubmitted);

    private class SenateSearchResponse
    {
        [JsonProperty("draw")]
        public int Draw { get; set; }

        [JsonProperty("recordsTotal")]
        public int RecordsTotal { get; set; }

        [JsonProperty("recordsFiltered")]
        public int RecordsFiltered { get; set; }

        [JsonProperty("data")]
        public List<List<string>> Data { get; set; } = [];

        [JsonProperty("result")]
        public string Result { get; set; }
    }
}
