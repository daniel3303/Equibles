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
                    McpToolExecutor.UtcMonthsAgo(3)
                );

                maxResults = McpLimit.Clamp(maxResults);

                var records = await _putCallRepository
                    .GetByType(ratioType, start, end)
                    .OnlyReconcilable()
                    .OrderByDescending(r => r.Date)
                    .Take(maxResults)
                    .ToListAsync();

                return MarkdownTable.Render(
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
                    McpToolExecutor.UtcMonthsAgo(3)
                );

                maxResults = McpLimit.Clamp(maxResults);

                var records = await _vixRepository
                    .GetByDateRange(start, end)
                    .OrderByDescending(v => v.Date)
                    .Take(maxResults)
                    .ToListAsync();

                return MarkdownTable.Render(
                    records.OrderBy(v => v.Date).ToList(),
                    "No VIX data found in the specified date range.",
                    "CBOE Volatility Index (VIX):",
                    "| Date | Open | High | Low | Close |",
                    "|------|------|------|-----|-------|",
                    v =>
                        $"| {v.Date:yyyy-MM-dd} | {McpFormat.Invariant(v.Open, "F2")} | {McpFormat.Invariant(v.High, "F2")} | {McpFormat.Invariant(v.Low, "F2")} | {McpFormat.Invariant(v.Close, "F2")} |"
                );
            },
            "GetVixHistory",
            $"startDate: {startDate}"
        );
    }
}
