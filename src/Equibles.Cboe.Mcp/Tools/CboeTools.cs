using System.ComponentModel;
using System.Text;
using Equibles.Cboe.Data.Models;
using Equibles.Cboe.Repositories;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Cboe.Mcp.Tools;

[McpServerToolType]
public class CboeTools {
    private readonly CboePutCallRatioRepository _putCallRepository;
    private readonly CboeVixDailyRepository _vixRepository;
    private readonly ErrorManager _errorManager;
    private readonly ILogger<CboeTools> _logger;

    public CboeTools(
        CboePutCallRatioRepository putCallRepository,
        CboeVixDailyRepository vixRepository,
        ErrorManager errorManager,
        ILogger<CboeTools> logger
    ) {
        _putCallRepository = putCallRepository;
        _vixRepository = vixRepository;
        _errorManager = errorManager;
        _logger = logger;
    }

    [McpServerTool(Name = "GetPutCallRatios")]
    [Description("Get CBOE put/call ratio data showing market sentiment. Available types: Total (all exchange), Equity, Index, Vix, Etp. High ratios (>1.0) indicate bearish sentiment; low ratios (<0.7) indicate bullish sentiment.")]
    public Task<string> GetPutCallRatios(
        [Description("Ratio type: Total, Equity, Index, Vix, Etp (default: Equity)")] string type = "Equity",
        [Description("Start date in YYYY-MM-DD format (defaults to 3 months ago)")] string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")] string endDate = null,
        [Description("Maximum number of records to return (default: 60, newest first)")] int maxResults = 60
    ) {
        return McpToolExecutor.Execute(async () => {
            if (!Enum.TryParse<CboePutCallRatioType>(type, true, out var ratioType))
                return $"Invalid type '{type}'. Valid types: Total, Equity, Index, Vix, Etp";

            var start = !string.IsNullOrEmpty(startDate) && DateOnly.TryParse(startDate, out var parsedStart)
                ? parsedStart
                : DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3));

            var end = !string.IsNullOrEmpty(endDate) && DateOnly.TryParse(endDate, out var parsedEnd)
                ? parsedEnd
                : DateOnly.FromDateTime(DateTime.UtcNow);

            var records = await _putCallRepository.GetByType(ratioType, start, end)
                .OrderByDescending(r => r.Date)
                .Take(maxResults)
                .ToListAsync();

            if (records.Count == 0) return $"No put/call ratio data found for {ratioType.NameForHumans()} in the specified date range.";

            var result = new StringBuilder();
            result.AppendLine($"CBOE {ratioType.NameForHumans()} Put/Call Ratios:");
            result.AppendLine();
            result.AppendLine("| Date | Call Volume | Put Volume | Total | P/C Ratio |");
            result.AppendLine("|------|-----------|----------|-------|-----------|");

            foreach (var r in records.OrderBy(r => r.Date)) {
                var callStr = r.CallVolume?.ToString("N0") ?? "—";
                var putStr = r.PutVolume?.ToString("N0") ?? "—";
                var totalStr = r.TotalVolume?.ToString("N0") ?? "—";
                var ratioStr = r.PutCallRatio?.ToString("F2") ?? "—";
                result.AppendLine($"| {r.Date:yyyy-MM-dd} | {callStr} | {putStr} | {totalStr} | {ratioStr} |");
            }

            return result.ToString();
        }, _logger, "GetPutCallRatios", $"type: {type}", ReportError);
    }

    [McpServerTool(Name = "GetVixHistory")]
    [Description("Get CBOE Volatility Index (VIX) historical daily OHLC data. VIX measures expected 30-day S&P 500 volatility. Below 15 = low volatility/complacency, above 30 = high fear/uncertainty. Data available from 1990 to present.")]
    public Task<string> GetVixHistory(
        [Description("Start date in YYYY-MM-DD format (defaults to 3 months ago)")] string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")] string endDate = null,
        [Description("Maximum number of records to return (default: 60, newest first)")] int maxResults = 60
    ) {
        return McpToolExecutor.Execute(async () => {
            var start = !string.IsNullOrEmpty(startDate) && DateOnly.TryParse(startDate, out var parsedStart)
                ? parsedStart
                : DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3));

            var end = !string.IsNullOrEmpty(endDate) && DateOnly.TryParse(endDate, out var parsedEnd)
                ? parsedEnd
                : DateOnly.FromDateTime(DateTime.UtcNow);

            var records = await _vixRepository.GetByDateRange(start, end)
                .OrderByDescending(v => v.Date)
                .Take(maxResults)
                .ToListAsync();

            if (records.Count == 0) return "No VIX data found in the specified date range.";

            var result = new StringBuilder();
            result.AppendLine("CBOE Volatility Index (VIX):");
            result.AppendLine();
            result.AppendLine("| Date | Open | High | Low | Close |");
            result.AppendLine("|------|------|------|-----|-------|");

            foreach (var v in records.OrderBy(v => v.Date)) {
                result.AppendLine($"| {v.Date:yyyy-MM-dd} | {v.Open:F2} | {v.High:F2} | {v.Low:F2} | {v.Close:F2} |");
            }

            return result.ToString();
        }, _logger, "GetVixHistory", $"startDate: {startDate}", ReportError);
    }

    private Task ReportError(string toolName, string message, string stackTrace, string context) {
        return _errorManager.Create(ErrorSource.McpTool, toolName, message, stackTrace, context);
    }
}
