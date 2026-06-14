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
        "Get scheduled FDA advisory-committee (AdComm) meetings — the regulatory catalyst dates that move biotech and pharma stocks. Sourced from the FDA.gov advisory-committee calendar. Defaults to meetings in the next 90 days; pass a date range to look further ahead or back."
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
                var start = McpToolExecutor.ParseDateOr(startDate, today);
                var end = McpToolExecutor.ParseDateOr(endDate, start.AddDays(90));
                if (end < start)
                    end = start;

                maxResults = McpLimit.Clamp(maxResults);

                var records = await _repository
                    .GetByDateRange(start, end)
                    .OrderBy(c => c.MeetingDate)
                    .ThenBy(c => c.Title)
                    .Take(maxResults)
                    .ToListAsync();

                return MarkdownTable.Render(
                    records,
                    "No FDA advisory-committee meetings found in the specified date range.",
                    "FDA Catalyst Calendar (advisory-committee meetings):",
                    "| Date | Meeting | Center | Type | Through |",
                    "|------|---------|--------|------|---------|",
                    c =>
                        $"| {c.MeetingDate:yyyy-MM-dd} | {Clean(c.Title)} | {Clean(c.Center)} | {c.CatalystType.NameForHumans()} | {(c.EndDate.HasValue ? c.EndDate.Value.ToString("yyyy-MM-dd") : "—")} |"
                );
            },
            "GetFdaCatalysts",
            $"startDate: {startDate}"
        );
    }

    // Free-text columns can contain pipes or newlines that would break the markdown row.
    private static string Clean(string value) =>
        string.IsNullOrEmpty(value)
            ? value
            : value.Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
}
