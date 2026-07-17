using System.ComponentModel;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.FdaCatalysts.Repositories;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.FdaCatalysts.Mcp.Tools;

[McpServerToolType]
public class FdaCatalystTools
{
    private readonly FdaCatalystRepository _repository;
    private readonly McpToolRunner _runner;

    public FdaCatalystTools(
        FdaCatalystRepository repository,
        ErrorManager errorManager,
        ILogger<FdaCatalystTools> logger
    )
    {
        _repository = repository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetFdaCatalysts")]
    [Description(
        "Get scheduled FDA advisory-committee (AdComm) meetings, sourced from the FDA.gov advisory-committee calendar, each with a link to its FDA meeting page. Defaults to meetings in the next 90 days; pass a date range to look further ahead. This is a forward-looking calendar of announced meetings, not a historical archive — coverage starts in late 2025 — and entries are the FDA's own listings, not linked to stock tickers."
    )]
    public Task<string> GetFdaCatalysts(
        [Description("Start date in YYYY-MM-DD format (defaults to today)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to 90 days after the start)")]
            string endDate = null,
        [Description("Maximum number of meetings to return (default: 60, soonest first)")]
            int maxResults = 60
    )
    {
        return _runner.Execute(
            async () =>
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var start = today;
                if (!string.IsNullOrWhiteSpace(startDate))
                {
                    if (!McpOutput.TryParseDate(startDate, out var parsedStart))
                        return McpOutput.InvalidArgument("startDate", startDate, "YYYY-MM-DD");
                    start = DateOnly.FromDateTime(parsedStart);
                }

                var end = start.AddDays(90);
                if (!string.IsNullOrWhiteSpace(endDate))
                {
                    if (!McpOutput.TryParseDate(endDate, out var parsedEnd))
                        return McpOutput.InvalidArgument("endDate", endDate, "YYYY-MM-DD");
                    end = DateOnly.FromDateTime(parsedEnd);
                }

                // An inverted range is a caller mistake — clamping it silently searched a
                // different window than requested and reported a misleading "no meetings".
                if (end < start)
                    return $"Invalid date range: endDate {end:yyyy-MM-dd} is before startDate {start:yyyy-MM-dd}.";

                maxResults = McpLimit.Clamp(maxResults);

                var range = _repository.GetByDateRange(start, end);
                var total = await range.CountAsync();
                var records = await range
                    .OrderBy(c => c.MeetingDate)
                    .ThenBy(c => c.Title)
                    .Take(maxResults)
                    .ToListAsync();

                if (records.Count == 0)
                    return await EmptyRangeMessage(start, end);

                var result = MarkdownTable.Render(
                    records,
                    string.Empty,
                    $"FDA Catalyst Calendar (advisory-committee meetings, {start:yyyy-MM-dd} to {end:yyyy-MM-dd}):",
                    "| Date | Meeting | Center | Type | Through | Details |",
                    "|------|---------|--------|------|---------|---------|",
                    c =>
                        $"| {c.MeetingDate:yyyy-MM-dd} | {Clean(c.Title)} | {Clean(c.Center)} | {c.CatalystType.NameForHumans()} | {(c.EndDate.HasValue ? c.EndDate.Value.ToString("yyyy-MM-dd") : "—")} | {(string.IsNullOrEmpty(c.SourceUrl) ? "—" : c.SourceUrl)} |"
                );
                var truncation = McpOutput.TruncationNote(records.Count, total);
                return truncation.Length == 0 ? result : result + "\n" + truncation + "\n";
            },
            "GetFdaCatalysts",
            $"startDate: {startDate}"
        );
    }

    // Reports the searched window plus the calendar's actual coverage, so a query
    // outside the covered span reads as a coverage boundary, never as "no meetings".
    private async Task<string> EmptyRangeMessage(DateOnly start, DateOnly end)
    {
        var oldest = await _repository.GetAll().MinAsync(c => (DateOnly?)c.MeetingDate);
        var newest = await _repository.GetAll().MaxAsync(c => (DateOnly?)c.MeetingDate);
        var message =
            $"No FDA advisory-committee meetings found between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}.";
        return oldest == null || newest == null
            ? message
            : message
                + $" The calendar currently covers {oldest:yyyy-MM-dd} to {newest:yyyy-MM-dd} — meetings outside that span have not been announced or predate coverage.";
    }

    // Free-text columns can contain pipes or newlines that would break the markdown row.
    private static string Clean(string value) =>
        string.IsNullOrEmpty(value)
            ? value
            : value.Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
}
