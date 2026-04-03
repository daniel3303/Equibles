using System.ComponentModel;
using System.Text;
using Equibles.CommonStocks.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Finra.Repositories;
using Equibles.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Finra.Mcp.Tools;

[McpServerToolType]
public class ShortDataTools {
    private readonly DailyShortVolumeRepository _shortVolumeRepository;
    private readonly ShortInterestRepository _shortInterestRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly ErrorManager _errorManager;
    private readonly ILogger<ShortDataTools> _logger;

    public ShortDataTools(
        DailyShortVolumeRepository shortVolumeRepository,
        ShortInterestRepository shortInterestRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<ShortDataTools> logger
    ) {
        _shortVolumeRepository = shortVolumeRepository;
        _shortInterestRepository = shortInterestRepository;
        _commonStockRepository = commonStockRepository;
        _errorManager = errorManager;
        _logger = logger;
    }

    [McpServerTool(Name = "GetShortVolume")]
    [Description("Get daily short volume data for a stock from FINRA. Shows short volume, exempt volume, total volume, and short volume percentage. High short volume % (>50%) may indicate bearish pressure.")]
    public Task<string> GetShortVolume(
        [Description("Stock ticker symbol (e.g., AAPL, GME, AMC)")] string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 3 months ago)")] string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")] string endDate = null,
        [Description("Maximum number of records to return (default: 90, newest first)")] int maxResults = 90
    ) {
        return McpToolExecutor.Execute(async () => {
            var stock = await _commonStockRepository.GetByTicker(ticker.Trim().ToUpperInvariant());
            if (stock == null) return $"Stock '{ticker}' not found.";

            var query = _shortVolumeRepository.GetHistoryByStock(stock);

            var start = !string.IsNullOrEmpty(startDate) && DateOnly.TryParse(startDate, out var parsedStart)
                ? parsedStart
                : DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3));

            var end = !string.IsNullOrEmpty(endDate) && DateOnly.TryParse(endDate, out var parsedEnd)
                ? parsedEnd
                : DateOnly.FromDateTime(DateTime.UtcNow);

            query = query.Where(d => d.Date >= start && d.Date <= end);

            var records = await query
                .OrderByDescending(d => d.Date)
                .Take(maxResults)
                .ToListAsync();

            if (records.Count == 0) return $"No short volume data found for {stock.Ticker} in the specified date range.";

            var result = new StringBuilder();
            result.AppendLine($"Daily short volume for {stock.Ticker} ({stock.Name}):");
            result.AppendLine();
            result.AppendLine("| Date | Short Volume | Exempt | Total Volume | Short % |");
            result.AppendLine("|------|-------------|--------|-------------|---------|");

            foreach (var r in records.OrderBy(r => r.Date)) {
                var shortPct = r.TotalVolume > 0 ? (double)r.ShortVolume / r.TotalVolume * 100 : 0;
                result.AppendLine($"| {r.Date:yyyy-MM-dd} | {r.ShortVolume:N0} | {r.ShortExemptVolume:N0} | {r.TotalVolume:N0} | {shortPct:F1}% |");
            }

            return result.ToString();
        }, _logger, "GetShortVolume", $"ticker: {ticker}", ReportError);
    }

    [McpServerTool(Name = "GetShortInterest")]
    [Description("Get short interest data for a stock from FINRA. Shows current short position, change from previous period, average daily volume, and days to cover. Published bi-monthly. High days-to-cover (>5) suggests a potential short squeeze.")]
    public Task<string> GetShortInterest(
        [Description("Stock ticker symbol (e.g., AAPL, GME, TSLA)")] string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")] string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")] string endDate = null,
        [Description("Maximum number of records to return (default: 24, newest first)")] int maxResults = 24
    ) {
        return McpToolExecutor.Execute(async () => {
            var stock = await _commonStockRepository.GetByTicker(ticker.Trim().ToUpperInvariant());
            if (stock == null) return $"Stock '{ticker}' not found.";

            var query = _shortInterestRepository.GetHistoryByStock(stock);

            var start = !string.IsNullOrEmpty(startDate) && DateOnly.TryParse(startDate, out var parsedStart)
                ? parsedStart
                : DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));

            var end = !string.IsNullOrEmpty(endDate) && DateOnly.TryParse(endDate, out var parsedEnd)
                ? parsedEnd
                : DateOnly.FromDateTime(DateTime.UtcNow);

            query = query.Where(s => s.SettlementDate >= start && s.SettlementDate <= end);

            var records = await query
                .OrderByDescending(s => s.SettlementDate)
                .Take(maxResults)
                .ToListAsync();

            if (records.Count == 0) return $"No short interest data found for {stock.Ticker} in the specified date range.";

            var result = new StringBuilder();
            result.AppendLine($"Short interest for {stock.Ticker} ({stock.Name}):");
            result.AppendLine();
            result.AppendLine("| Settlement Date | Short Position | Change | Avg Daily Volume | Days to Cover |");
            result.AppendLine("|----------------|---------------|--------|-----------------|---------------|");

            foreach (var r in records.OrderBy(r => r.SettlementDate)) {
                var changeStr = r.ChangeInShortPosition >= 0 ? $"+{r.ChangeInShortPosition:N0}" : r.ChangeInShortPosition.ToString("N0");
                var advStr = r.AverageDailyVolume?.ToString("N0") ?? "—";
                var dtcStr = r.DaysToCover?.ToString("F1") ?? "—";
                result.AppendLine($"| {r.SettlementDate:yyyy-MM-dd} | {r.CurrentShortPosition:N0} | {changeStr} | {advStr} | {dtcStr} |");
            }

            return result.ToString();
        }, _logger, "GetShortInterest", $"ticker: {ticker}", ReportError);
    }

    [McpServerTool(Name = "GetShortInterestSnapshot")]
    [Description("Get the latest short interest data across all stocks, sorted by days to cover (descending). Useful for finding stocks with high short interest that may be prone to short squeezes.")]
    public Task<string> GetShortInterestSnapshot(
        [Description("Minimum days to cover filter (default: 0)")] decimal minDaysToCover = 0,
        [Description("Maximum number of results to return (default: 50)")] int maxResults = 50
    ) {
        return McpToolExecutor.Execute(async () => {
            var latestDate = await _shortInterestRepository.GetLatestSettlementDate().FirstOrDefaultAsync();
            if (latestDate == default) return "No short interest data available.";

            var query = _shortInterestRepository.GetBySettlementDate(latestDate)
                .Include(s => s.CommonStock)
                .Where(s => s.DaysToCover != null);

            if (minDaysToCover > 0) {
                query = query.Where(s => s.DaysToCover >= minDaysToCover);
            }

            var records = await query
                .OrderByDescending(s => s.DaysToCover)
                .Take(maxResults)
                .ToListAsync();

            if (records.Count == 0) return $"No short interest data found for settlement date {latestDate:yyyy-MM-dd} with days to cover >= {minDaysToCover}.";

            var result = new StringBuilder();
            result.AppendLine($"Short interest snapshot — settlement date {latestDate:yyyy-MM-dd}:");
            result.AppendLine();
            result.AppendLine("| Ticker | Short Position | Change | Avg Daily Volume | Days to Cover |");
            result.AppendLine("|--------|---------------|--------|-----------------|---------------|");

            foreach (var r in records) {
                var changeStr = r.ChangeInShortPosition >= 0 ? $"+{r.ChangeInShortPosition:N0}" : r.ChangeInShortPosition.ToString("N0");
                var advStr = r.AverageDailyVolume?.ToString("N0") ?? "—";
                result.AppendLine($"| {r.CommonStock.Ticker} | {r.CurrentShortPosition:N0} | {changeStr} | {advStr} | {r.DaysToCover:F1} |");
            }

            return result.ToString();
        }, _logger, "GetShortInterestSnapshot", $"minDaysToCover: {minDaysToCover}", ReportError);
    }

    private Task ReportError(string toolName, string message, string stackTrace, string context) {
        return _errorManager.Create(ErrorSource.McpTool, toolName, message, stackTrace, context);
    }
}
