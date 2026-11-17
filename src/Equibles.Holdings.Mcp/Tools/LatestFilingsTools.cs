using System.ComponentModel;
using System.Globalization;
using System.Text;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Mcp.Helpers;
using Equibles.Holdings.Mcp.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Holdings.Mcp.Tools;

[McpServerToolType]
public class LatestFilingsTools
{
    // Filing days the header counts; a deadline week is the widest burst worth seeing at once.
    private const int FilingDaysSummarized = 5;

    // Calendar days behind the newest filing day the header reads, so its counts and newest load
    // come from an index range instead of the whole rollup.
    private const int SummaryWindowDays = 14;

    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly McpToolRunner _runner;

    public LatestFilingsTools(
        InstitutionalHoldingRepository holdingRepository,
        ErrorManager errorManager,
        ILogger<LatestFilingsTools> logger
    )
    {
        _holdingRepository = holdingRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetLatest13FFilings", Title = "Latest 13F Filings", ReadOnly = true)]
    [Description(
        "List the newest SEC 13F-HR filings loaded here, newest filing day first: filer, CIK, report quarter, SEC filing date, when the filing was last loaded (UTC), original or amendment, tracked positions and tracked value. Opens with how many filings are loaded per recent filing day and when the newest was loaded, so you can tell whether a quarter's filings are in before reading a filer's holdings. Filter by reportDate (a quarter end) or filedSince. A filing appears only after it is filed with the SEC and loaded; a filer absent from the list has no newer 13F on file here."
    )]
    public Task<string> GetLatest13FFilings(
        [Description(
            "Quarter-end 13F report date in YYYY-MM-DD format to list only that quarter's filings (omit for every quarter)"
        )]
            string reportDate = null,
        [Description("Earliest SEC filing date in YYYY-MM-DD format (omit for no lower bound)")]
            string filedSince = null,
        [Description("Maximum number of filings to return (default: 20, clamped to 1-500)")]
            int maxResults = 20,
        [Description(
            "Number of filings to skip before returning rows (default: 0, clamped to 0-100000). For the next page, add the returned row count to the current offset."
        )]
            int offset = 0
    )
    {
        return _runner.Execute(
            async () =>
            {
                DateOnly? quarter = null;
                if (!string.IsNullOrWhiteSpace(reportDate))
                {
                    if (
                        !TryParseDate(reportDate, out var parsedQuarter)
                        || !HoldingsCorpusCoverage.IsCalendarQuarterEnd(parsedQuarter)
                    )
                        return $"reportDate '{reportDate}' is not a calendar quarter end. Use YYYY-MM-DD, e.g. 2026-09-30.";
                    quarter = parsedQuarter;
                }
                DateOnly? since = null;
                if (!string.IsNullOrWhiteSpace(filedSince))
                {
                    if (!TryParseDate(filedSince, out var parsedSince))
                        return $"Could not parse filedSince '{filedSince}'. Use YYYY-MM-DD.";
                    since = parsedSince;
                }

                maxResults = McpLimit.Clamp(maxResults);
                offset = McpLimit.ClampOffset(offset);

                var filings = _holdingRepository.GetRecentFilings();
                if (quarter is { } quarterEnd)
                    // A quarter's 13F is filed after the quarter ends; the bound keeps the
                    // filter on the filing-date index.
                    filings = filings.Where(f =>
                        f.ReportDate == quarterEnd && f.FilingDate > quarterEnd
                    );
                if (since is { } sinceDate)
                    filings = filings.Where(f => f.FilingDate >= sinceDate);

                // One row past the page tells whether another page exists without counting the
                // whole rollup.
                var page = await filings
                    .OrderByDescending(f => f.FilingDate)
                    .ThenByDescending(f => f.AccessionNumber)
                    .Skip(offset)
                    .Take(maxResults + 1)
                    .AsNoTracking()
                    .ToListAsync();
                if (page.Count == 0)
                    return offset > 0
                        ? $"No filings at offset {offset}; lower offset."
                        : "No 13F filings match the filters.";
                var hasMore = page.Count > maxResults;
                if (hasMore)
                    page.RemoveAt(page.Count - 1);

                var summary = await ReadSummary(filings);
                return Render(page, offset, hasMore, summary, quarter);
            },
            "GetLatest13FFilings",
            $"reportDate: {reportDate}, filedSince: {filedSince}, maxResults: {maxResults}, offset: {offset}"
        );
    }

    private static async Task<LatestFilingsSummary> ReadSummary(IQueryable<RecentFiling> filings)
    {
        var newestDay = await filings.MaxAsync(f => (DateOnly?)f.FilingDate);
        if (newestDay is not { } newest)
            return new LatestFilingsSummary([], null);
        var windowStart = newest.AddDays(-SummaryWindowDays);
        var window = filings.Where(f => f.FilingDate >= windowStart);
        var days = await window
            .GroupBy(f => f.FilingDate)
            .Select(group => new { Day = group.Key, Count = group.Count() })
            .OrderByDescending(row => row.Day)
            .Take(FilingDaysSummarized)
            .ToListAsync();
        var newestLoad = await window.MaxAsync(f => (DateTime?)f.ImportedAt);
        return new LatestFilingsSummary(
            days.Select(row => (row.Day, row.Count)).ToList(),
            newestLoad
        );
    }

    public static string Render(
        IReadOnlyList<RecentFiling> page,
        int offset,
        bool hasMore,
        LatestFilingsSummary summary,
        DateOnly? quarter
    )
    {
        var result = new StringBuilder();
        result.AppendLine(
            quarter is { } quarterEnd
                ? $"Latest 13F filings loaded for {FormatDate(quarterEnd)}, newest filing day first:"
                : "Latest 13F filings loaded, newest filing day first:"
        );
        if (summary.FilingDays.Count > 0)
            result.AppendLine(
                "Loaded by SEC filing day: "
                    + string.Join(
                        "; ",
                        summary.FilingDays.Select(day =>
                            $"{FormatDate(day.Day)}: {McpFormat.WholeNumber(day.Count)}"
                        )
                    )
                    + "."
            );
        if (summary.NewestLoad is { } newestLoad)
            result.AppendLine($"Most recent load: {HolderFilingNotes.LoadedAt(newestLoad)}.");
        result.AppendLine();
        result.AppendLine(
            "| # | Filer | CIK | Report date | Filed | Last loaded (UTC) | Form | Positions | Value ($M) |"
        );
        result.AppendLine(
            "|---|-------|-----|-------------|-------|-------------------|------|-----------|-----------|"
        );
        for (var index = 0; index < page.Count; index++)
        {
            var filing = page[index];
            result.AppendLine(
                $"| {offset + index + 1} | {MarkdownTable.EscapeCell(filing.FilerName)} | {filing.FilerCik} | "
                    + $"{FormatDate(filing.ReportDate)} | {FormatDate(filing.FilingDate)} | "
                    + $"{McpFormat.Invariant(filing.ImportedAt, "yyyy-MM-dd HH:mm")} | "
                    + $"{(filing.IsAmendment ? "13F-HR/A" : "13F-HR")} | "
                    + $"{McpFormat.WholeNumber(filing.PositionCount)} | "
                    + $"{McpFormat.Invariant(filing.TotalValue / 1_000_000m, "N1")} |"
            );
        }
        result.AppendLine();
        result.AppendLine(
            "_Filed is the SEC filing date. Last loaded is when the filing's holdings were last written here; reprocessing a filing updates it. Positions and value count the tracked U.S.-listed stocks and their options. A 13F is due 45 days after the quarter end, so a filer missing from this list has no newer 13F on file here._"
        );
        var last = offset + page.Count;
        result.AppendLine(
            hasMore
                ? $"_Showing filings {offset + 1}-{last} - pass offset={last} for older filings._"
                : $"_Showing filings {offset + 1}-{last} (the last page)._"
        );
        return result.ToString();
    }

    private static bool TryParseDate(string input, out DateOnly date) =>
        DateOnly.TryParseExact(
            input.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date
        );

    private static string FormatDate(DateOnly date) => McpFormat.Invariant(date, "yyyy-MM-dd");
}
