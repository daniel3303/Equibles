using System.ComponentModel;
using System.Globalization;
using Equibles.Cboe.Data.Models;
using Equibles.Cboe.Repositories;
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

    [McpServerTool(Name = "GetPutCallRatios")]
    [Description(
        "Get CBOE put/call ratio data showing market sentiment. Available types: Total (all exchange), Equity, Index, Vix, Etp. High ratios (>1.0) indicate bearish sentiment; low ratios (<0.7) indicate bullish sentiment."
    )]
    public Task<string> GetPutCallRatios(
        [Description("Ratio type: Total, Equity, Index, Vix, Etp (default: Equity)")]
            string type = "Equity",
        [Description("Start date in YYYY-MM-DD format (defaults to 3 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Maximum number of records to return (default: 60, newest first)")]
            int maxResults = 60
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (!Enum.TryParse<CboePutCallRatioType>(type, true, out var ratioType))
                    return $"Invalid type '{type}'. Valid types: Total, Equity, Index, Vix, Etp";

                var (start, end) = McpToolExecutor.ParseDateRange(
                    startDate,
                    endDate,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3))
                );

                // A negative maxResults would flow into .Take(...) as a negative SQL LIMIT,
                // which PostgreSQL rejects and surfaces as the internal-error sentinel. Clamp
                // so a non-positive cap yields zero rows and the existing no-data message.
                maxResults = Math.Max(0, maxResults);

                var records = await _putCallRepository
                    .GetByType(ratioType, start, end)
                    .OrderByDescending(r => r.Date)
                    .Take(maxResults)
                    .ToListAsync();

                if (records.Count == 0)
                    return $"No put/call ratio data found for {ratioType.NameForHumans()} in the specified date range.";

                var result = MarkdownTable.Start(
                    $"CBOE {ratioType.NameForHumans()} Put/Call Ratios:",
                    "| Date | Call Volume | Put Volume | Total | P/C Ratio |",
                    "|------|-----------|----------|-------|-----------|"
                );

                foreach (var r in records.OrderBy(r => r.Date))
                {
                    var callStr = McpFormat.OrDash(r.CallVolume, "N0");
                    var putStr = McpFormat.OrDash(r.PutVolume, "N0");
                    var totalStr = McpFormat.OrDash(r.TotalVolume, "N0");
                    var ratioStr = McpFormat.OrDash(r.PutCallRatio, "F2");
                    result.AppendLine(
                        $"| {r.Date:yyyy-MM-dd} | {callStr} | {putStr} | {totalStr} | {ratioStr} |"
                    );
                }

                return result.ToString();
            },
            "GetPutCallRatios",
            $"type: {type}"
        );
    }

    [McpServerTool(Name = "GetVixHistory")]
    [Description(
        "Get CBOE Volatility Index (VIX) historical daily OHLC data. VIX measures expected 30-day S&P 500 volatility. Below 15 = low volatility/complacency, above 30 = high fear/uncertainty. Data available from 1990 to present."
    )]
    public Task<string> GetVixHistory(
        [Description("Start date in YYYY-MM-DD format (defaults to 3 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Maximum number of records to return (default: 60, newest first)")]
            int maxResults = 60
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (start, end) = McpToolExecutor.ParseDateRange(
                    startDate,
                    endDate,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3))
                );

                // A negative maxResults would flow into .Take(...) as a negative SQL LIMIT,
                // which PostgreSQL rejects and surfaces as the internal-error sentinel. Clamp
                // so a non-positive cap yields zero rows and the existing no-data message.
                maxResults = Math.Max(0, maxResults);

                var records = await _vixRepository
                    .GetByDateRange(start, end)
                    .OrderByDescending(v => v.Date)
                    .Take(maxResults)
                    .ToListAsync();

                if (records.Count == 0)
                    return "No VIX data found in the specified date range.";

                var result = MarkdownTable.Start(
                    "CBOE Volatility Index (VIX):",
                    "| Date | Open | High | Low | Close |",
                    "|------|------|------|-----|-------|"
                );

                foreach (var v in records.OrderBy(v => v.Date))
                {
                    result.AppendLine(
                        $"| {v.Date:yyyy-MM-dd} | {v.Open.ToString("F2", CultureInfo.InvariantCulture)} | {v.High.ToString("F2", CultureInfo.InvariantCulture)} | {v.Low.ToString("F2", CultureInfo.InvariantCulture)} | {v.Close.ToString("F2", CultureInfo.InvariantCulture)} |"
                    );
                }

                return result.ToString();
            },
            "GetVixHistory",
            $"startDate: {startDate}"
        );
    }
}
