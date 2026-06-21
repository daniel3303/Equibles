using System.Globalization;
using System.Text.RegularExpressions;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Common.Retry;
using HtmlAgilityPack;
using Newtonsoft.Json;
using static Equibles.Congress.HostedService.Services.DisclosureParsingHelper;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Searches and parses Senate eFD annual financial disclosure reports.
/// Electronic filings render as HTML pages (Part 3 = assets, Part 7 =
/// liabilities); scanned paper filings live under "/view/paper/" URLs and are
/// skipped, so a missing report means "no electronic filing", not zero net
/// worth. The coverage year and the amendment marker come from the search
/// result's link text ("Annual Report for CY 2024 (Amendment 1)").
/// </summary>
[Service]
public partial class SenateAnnualReportClient
{
    private static readonly IRateLimiter RateLimiter = new RateLimiter(
        maxRequests: 5,
        timeWindow: TimeSpan.FromSeconds(1)
    );
    private const int MaxRetries = 3;

    private const string BaseUrl = "https://efdsearch.senate.gov";
    private const string SearchDataUrl = BaseUrl + "/search/report/data/";
    private const string AnnualReportTypeFilter = "[7]"; // Annual Report
    private const string SenatorFilerTypeFilter = "[1]"; // Senator

    private readonly ISenateBrowserSession _session;
    private readonly ILogger<SenateAnnualReportClient> _logger;

    public SenateAnnualReportClient(
        ISenateBrowserSession session,
        ILogger<SenateAnnualReportClient> logger
    )
    {
        _session = session;
        _logger = logger;
    }

    /// <summary>
    /// Returns every electronically-filed senator annual report submitted in
    /// the window. Originals and amendments both appear; the caller keeps the
    /// latest filed report per member-year.
    /// </summary>
    public async Task<List<AnnualDisclosureReport>> GetAnnualReports(
        DateOnly submittedFrom,
        DateOnly submittedTo,
        CancellationToken ct
    )
    {
        await _session.EnsureAuthenticated(ct);

        var filings = await SearchAnnualReports(submittedFrom, submittedTo, ct);
        _logger.LogInformation(
            "Found {Count} Senate annual report filings submitted between {From} and {To}",
            filings.Count,
            submittedFrom,
            submittedTo
        );

        var reports = new List<AnnualDisclosureReport>();

        foreach (var filing in filings)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var report = await FetchAndParseReport(filing, ct);
                if (report != null)
                    reports.Add(report);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to parse Senate annual report for {Member} at {Url}",
                    filing.MemberName,
                    filing.ReportUrl
                );
            }
        }

        _logger.LogInformation(
            "Parsed {Parsed} electronic Senate annual reports out of {Total} filings",
            reports.Count,
            filings.Count
        );
        return reports;
    }

    private async Task<List<SenateAnnualFiling>> SearchAnnualReports(
        DateOnly from,
        DateOnly to,
        CancellationToken ct
    )
    {
        var filings = new List<SenateAnnualFiling>();
        var start = 0;
        const int pageSize = 100;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var formFields = new Dictionary<string, string>
            {
                ["report_types"] = AnnualReportTypeFilter,
                ["filer_types"] = SenatorFilerTypeFilter,
                // eFD expects US-format dates; "/" in a custom format is the
                // culture date-separator placeholder, so pin the invariant
                // culture or a de-DE host posts "01.01.2025" (GH-3660).
                ["submitted_start_date"] =
                    $"{from.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)} 00:00:00",
                ["submitted_end_date"] =
                    $"{to.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)} 23:59:59",
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
                var filing = ParseSearchRow(row);
                if (filing != null)
                    filings.Add(filing);
            }

            _logger.LogDebug(
                "Senate annual report search: fetched {Start}-{End} of {Total}",
                start,
                start + result.Data.Count,
                result.RecordsTotal
            );

            start += pageSize;
            if (start >= result.RecordsTotal)
                break;
        }

        return filings;
    }

    internal static SenateAnnualFiling ParseSearchRow(List<string> row)
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

        // The coverage year and the amendment marker only exist in the link
        // text; rows that are not annual reports (e.g. candidate reports under
        // the same report-type filter) never match.
        var titleMatch = AnnualReportTitleRegex().Match(linkHtml);
        if (!titleMatch.Success)
            return null;

        var reportPath = hrefMatch.Groups[1].Value;
        var reportUrl = reportPath.StartsWith("http") ? reportPath : BaseUrl + reportPath;

        if (!IsValidDisclosureUrl(reportUrl, BaseUrl))
            return null;

        // Skip paper filings (scanned images) — only HTML electronic filings
        // are parseable.
        if (reportUrl.Contains("/view/paper/", StringComparison.OrdinalIgnoreCase))
            return null;

        // eFD dates are US-format MM/dd/yyyy — parse culture-pinned, not with
        // the host culture.
        var dateSubmitted = ParseDate(row[4]?.Trim());
        if (dateSubmitted == null)
            return null;

        return new SenateAnnualFiling(
            memberName,
            reportUrl,
            int.Parse(titleMatch.Groups[1].Value),
            dateSubmitted.Value,
            titleMatch.Groups[2].Success
        );
    }

    private async Task<AnnualDisclosureReport> FetchAndParseReport(
        SenateAnnualFiling filing,
        CancellationToken ct
    )
    {
        var html = await FetchWithRetry(filing.ReportUrl, ct: ct);
        var lines = ParseAnnualReportHtml(html);

        if (lines == null)
        {
            _logger.LogWarning(
                "Senate annual report at {Url} has no recognizable schedule layout; skipping",
                filing.ReportUrl
            );
            return null;
        }

        return new AnnualDisclosureReport
        {
            MemberName = filing.MemberName,
            Position = CongressPosition.Senator,
            Year = filing.Year,
            FiledDate = filing.DateSubmitted,
            ReportId = ExtractReportId(filing.ReportUrl),
            IsAmendment = filing.IsAmendment,
            Lines = lines,
        };
    }

    // The eFD report id is the GUID path segment: /search/view/annual/{id}/.
    internal static string ExtractReportId(string reportUrl) =>
        reportUrl.TrimEnd('/').Split('/')[^1];

    /// <summary>
    /// Parses the Part 3 (assets) and Part 7 (liabilities) tables out of an
    /// e-filed annual report page. Returns null when the page carries no
    /// "Part 3. Assets" heading — an unrecognized layout that must never be
    /// read as a zero-asset report.
    /// </summary>
    internal static List<AnnualDisclosureLineItem> ParseAnnualReportHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        if (!doc.DocumentNode.InnerText.Contains("Part 3. Assets"))
            return null;

        var items = new List<AnnualDisclosureLineItem>();
        var tables = doc.DocumentNode.SelectNodes("//table");
        if (tables == null)
            return items;

        foreach (var table in tables)
        {
            var headers = ExtractHeaderTexts(table);
            if (headers == null)
                continue;

            if (headers.Contains("asset") && headers.Contains("value"))
                ParseAssetTable(table, headers, items);
            else if (headers.Contains("creditor") && headers.Contains("amount"))
                ParseLiabilityTable(table, headers, items);
        }

        return items;
    }

    private static List<string> ExtractHeaderTexts(HtmlNode table)
    {
        var headerNodes = table.SelectNodes(".//thead//th") ?? table.SelectNodes(".//tr[1]//th");
        return headerNodes
            ?.Select(h => HtmlEntity.DeEntitize(h.InnerText).Trim().ToLowerInvariant())
            .ToList();
    }

    private static void ParseAssetTable(
        HtmlNode table,
        List<string> headers,
        List<AnnualDisclosureLineItem> items
    )
    {
        var assetColumn = headers.IndexOf("asset");
        var valueColumn = headers.IndexOf("value");

        foreach (var row in DataRows(table))
        {
            var cells = row.SelectNodes(".//td");
            if (cells == null || cells.Count <= Math.Max(assetColumn, valueColumn))
                continue;

            var range = ParseRangeCell(CellText(cells[valueColumn]));
            if (range == null)
                continue;

            var description = CellText(cells[assetColumn]);
            if (string.IsNullOrEmpty(description))
                continue;

            items.Add(
                new AnnualDisclosureLineItem
                {
                    Kind = CongressionalDisclosureLineKind.Asset,
                    Description = Truncate(description, 512),
                    RangeMinimum = range.Value.from,
                    RangeMaximum = range.Value.to,
                }
            );
        }
    }

    private static void ParseLiabilityTable(
        HtmlNode table,
        List<string> headers,
        List<AnnualDisclosureLineItem> items
    )
    {
        var typeColumn = headers.IndexOf("type");
        var amountColumn = headers.IndexOf("amount");
        var creditorColumn = headers.IndexOf("creditor");

        foreach (var row in DataRows(table))
        {
            var cells = row.SelectNodes(".//td");
            if (
                cells == null
                || cells.Count <= Math.Max(amountColumn, Math.Max(typeColumn, creditorColumn))
            )
                continue;

            var range = ParseRangeCell(CellText(cells[amountColumn]));
            if (range == null)
                continue;

            // The entry gate only requires "creditor" and "amount"; a table
            // without a Type column leaves typeColumn at -1 (the bounds check
            // above already tolerates it), so read it only when present.
            var type = typeColumn >= 0 ? CellText(cells[typeColumn]) : "";
            var creditor = CellText(cells[creditorColumn]);
            var description = string.IsNullOrEmpty(creditor) ? type : $"{type} ({creditor})";
            if (string.IsNullOrEmpty(description))
                continue;

            items.Add(
                new AnnualDisclosureLineItem
                {
                    Kind = CongressionalDisclosureLineKind.Liability,
                    Description = Truncate(description, 512),
                    RangeMinimum = range.Value.from,
                    RangeMaximum = range.Value.to,
                }
            );
        }
    }

    private static IEnumerable<HtmlNode> DataRows(HtmlNode table) =>
        table.SelectNodes(".//tbody//tr") ?? table.SelectNodes(".//tr")?.Skip(1) ?? [];

    // The disclosed name lives in the cell's main text; ".muted" descendants
    // carry secondary metadata (location, provider, comments) that is not part
    // of the row's identity.
    private static string CellText(HtmlNode cell)
    {
        var clone = cell.CloneNode(deep: true);
        var muted = clone.SelectNodes(".//*[contains(@class, 'muted')]");
        if (muted != null)
        {
            foreach (var node in muted)
                node.Remove();
        }
        var text = HtmlEntity.DeEntitize(clone.InnerText);
        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    // A line item carries only the form's own brackets: "$X - $Y" or the
    // open-top "Over $X". Sentinels ("--", "None", "Unascertainable") and any
    // other cell content yield no line.
    private static (long from, long to)? ParseRangeCell(string text)
    {
        if (CleanSentinel(text) == null)
            return null;

        var amounts = AmountRegex().Matches(text).Count;
        var isParseableRange =
            amounts >= 2
            || (amounts == 1 && text.Contains("Over", StringComparison.OrdinalIgnoreCase));
        if (!isParseableRange)
            return null;

        var range = ParseAmountRange(text);
        return range is { from: 0, to: 0 } ? null : range;
    }

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
                var delay = RetryBackoff.Exponential(attempt);
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

            var isRetryable = result.Status == 429 || result.Status >= 500;
            if (isRetryable && attempt < MaxRetries)
            {
                var delay = RetryBackoff.Exponential(attempt);
                _logger.LogWarning(
                    "Senate eFD returned {Status} for {Url}, retrying in {Delay}s (attempt {Attempt}/{MaxRetries})",
                    result.Status,
                    url,
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                );

                if (result.Status == 429)
                    RateLimiter.PauseFor(delay);

                await Task.Delay(delay, ct);
                continue;
            }

            if (result.Status is < 200 or >= 300)
            {
                _logger.LogError(
                    "Senate eFD returned {Status} for {Url} after {Attempts} attempt(s)",
                    result.Status,
                    url,
                    attempt + 1
                );
                throw new HttpRequestException(
                    $"Senate eFD request failed with HTTP {result.Status}: {url}"
                );
            }

            return result.Body;
        }

        throw new HttpRequestException(
            $"Max retries ({MaxRetries}) exceeded for Senate eFD request: {url}"
        );
    }

    // "Annual Report for CY 2024" with an optional "(Amendment N)" suffix —
    // group 1 = coverage year, group 2 = amendment number when present.
    [GeneratedRegex(@"Annual Report for CY (\d{4})(?:\s*\(Amendment\s+(\d+)\))?")]
    private static partial Regex AnnualReportTitleRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    internal sealed record SenateAnnualFiling(
        string MemberName,
        string ReportUrl,
        int Year,
        DateOnly DateSubmitted,
        bool IsAmendment
    );
}
