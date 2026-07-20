using System.ComponentModel;
using Equibles.Cboe.Data.Models;
using Equibles.Cboe.Repositories;
using Equibles.Cboe.Repositories.Extensions;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Cboe.Mcp.Tools;

[McpServerToolType]
public class CboeTools
{
    private readonly CboePutCallRatioRepository _putCallRepository;
    private readonly CboeVixDailyRepository _vixRepository;
    private readonly McpToolRunner _runner;

    public CboeTools(
        CboePutCallRatioRepository putCallRepository,
        CboeVixDailyRepository vixRepository,
        ErrorManager errorManager,
        ILogger<CboeTools> logger
    )
    {
        _putCallRepository = putCallRepository;
        _vixRepository = vixRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetPutCallRatios", Title = "CBOE Put/Call Ratios", ReadOnly = true)]
    [Description(
        "Get CBOE put/call ratio data showing market sentiment. Available types: Total (all "
            + "exchange), Equity, Index, Vix, Etp. High ratios (>1.0) indicate bearish sentiment; "
            + "low ratios (<0.7) indicate bullish sentiment. Volumes are contract counts. Data "
            + "available from November 2006 to present (the Vix type from October 2019); "
            + "pre-2013 history is sampled roughly weekly rather than daily."
    )]
    public Task<string> GetPutCallRatios(
        [Description("Ratio type: Total, Equity, Index, Vix, Etp (default: Equity)")]
            string type = "Equity",
        [Description("Start date in YYYY-MM-DD format (defaults to 3 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description(
            "Maximum number of records to return (default: 60, max: 500). When the range holds more rows the newest are kept; rows are always listed oldest to newest."
        )]
            int maxResults = 60
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (!Enum.TryParse<CboePutCallRatioType>(type, true, out var ratioType))
                    return $"Invalid type '{type}'. Valid types: Total, Equity, Index, Vix, Etp";

                var rangeError = ParseRangeStrict(
                    startDate,
                    endDate,
                    McpToolExecutor.UtcMonthsAgo(3),
                    out var start,
                    out var end
                );
                if (rangeError != null)
                    return rangeError;

                maxResults = McpLimit.Clamp(maxResults);

                var rangeQuery = _putCallRepository
                    .GetByType(ratioType, start, end)
                    .OnlyReconcilable();
                var total = await rangeQuery.CountAsync();

                var records = await rangeQuery
                    .OrderByDescending(r => r.Date)
                    .Take(maxResults)
                    .ToListAsync();

                var table = MarkdownTable.Render(
                    records.OrderBy(r => r.Date).ToList(),
                    $"No put/call ratio data found for {ratioType.NameForHumans()} in the specified date range.",
                    $"CBOE {ratioType.NameForHumans()} Put/Call Ratios:",
                    "| Date | Call Volume | Put Volume | Total | P/C Ratio |",
                    "|------|-----------|----------|-------|-----------|",
                    r =>
                    {
                        var callStr = McpFormat.OrDash(r.CallVolume, "N0");
                        var putStr = McpFormat.OrDash(r.PutVolume, "N0");
                        var totalStr = McpFormat.OrDash(r.TotalVolume, "N0");
                        var ratioStr = McpFormat.OrDash(r.PutCallRatio, "F2");
                        return $"| {r.Date:yyyy-MM-dd} | {callStr} | {putStr} | {totalStr} | {ratioStr} |";
                    }
                );

                return AppendNewestKeptTruncationNote(table, records.Count, total);
            },
            "GetPutCallRatios",
            $"type: {type}"
        );
    }

    [McpServerTool(Name = "GetVixHistory", Title = "VIX Volatility Index History", ReadOnly = true)]
    [Description(
        "Get CBOE Volatility Index (VIX) historical daily OHLC data. VIX measures expected "
            + "30-day S&P 500 volatility. Below 15 = low volatility/complacency, above 30 = "
            + "high fear/uncertainty. Data available from 1990 to present."
    )]
    public Task<string> GetVixHistory(
        [Description("Start date in YYYY-MM-DD format (defaults to 3 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description(
            "Maximum number of records to return (default: 60, max: 500). When the range holds more rows the newest are kept; rows are always listed oldest to newest."
        )]
            int maxResults = 60
    )
    {
        return _runner.Execute(
            async () =>
            {
                var rangeError = ParseRangeStrict(
                    startDate,
                    endDate,
                    McpToolExecutor.UtcMonthsAgo(3),
                    out var start,
                    out var end
                );
                if (rangeError != null)
                    return rangeError;

                maxResults = McpLimit.Clamp(maxResults);

                var rangeQuery = _vixRepository.GetByDateRange(start, end);
                var total = await rangeQuery.CountAsync();

                var records = await rangeQuery
                    .OrderByDescending(v => v.Date)
                    .Take(maxResults)
                    .ToListAsync();

                var table = MarkdownTable.Render(
                    records.OrderBy(v => v.Date).ToList(),
                    "No VIX data found in the specified date range.",
                    "CBOE Volatility Index (VIX):",
                    "| Date | Open | High | Low | Close |",
                    "|------|------|------|-----|-------|",
                    v =>
                        $"| {v.Date:yyyy-MM-dd} | {McpFormat.Invariant(v.Open, "F2")} | {McpFormat.Invariant(v.High, "F2")} | {McpFormat.Invariant(v.Low, "F2")} | {McpFormat.Invariant(v.Close, "F2")} |"
                );

                return AppendNewestKeptTruncationNote(table, records.Count, total);
            },
            "GetVixHistory",
            $"startDate: {startDate}"
        );
    }

    // Strict argument parsing shared by the date-ranged CBOE tools: a non-empty date must
    // be exactly yyyy-MM-dd (no silent fallback to the default window), and an inverted
    // range is a caller error rather than an empty-looking result.
    private static string ParseRangeStrict(
        string startDate,
        string endDate,
        DateOnly defaultStart,
        out DateOnly start,
        out DateOnly end
    )
    {
        start = defaultStart;
        end = DateOnly.FromDateTime(DateTime.UtcNow);

        if (!string.IsNullOrWhiteSpace(startDate))
        {
            if (!McpOutput.TryParseDate(startDate, out var parsedStart))
                return McpOutput.InvalidArgument("startDate", startDate, "yyyy-MM-dd");
            start = DateOnly.FromDateTime(parsedStart);
        }

        if (!string.IsNullOrWhiteSpace(endDate))
        {
            if (!McpOutput.TryParseDate(endDate, out var parsedEnd))
                return McpOutput.InvalidArgument("endDate", endDate, "yyyy-MM-dd");
            end = DateOnly.FromDateTime(parsedEnd);
        }

        if (start > end)
            return $"startDate ({start:yyyy-MM-dd}) is after endDate ({end:yyyy-MM-dd}) - swap the dates.";

        return null;
    }

    // Appended after tables that keep the NEWEST rows but render oldest-to-newest, where
    // the shared "Showing first N" wording would point at the wrong end of the table.
    private static string AppendNewestKeptTruncationNote(string table, int shown, int total)
    {
        if (shown >= total)
            return table;
        return table
            + Environment.NewLine
            + $"_Showing the newest {shown} of {total} records in the range - raise maxResults or narrow the date range to see older rows._";
    }
}
