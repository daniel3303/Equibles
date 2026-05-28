using System.ComponentModel;
using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Finra.Repositories;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Finra.Mcp.Tools;

[McpServerToolType]
public class ShortDataTools
{
    private readonly DailyShortVolumeRepository _shortVolumeRepository;
    private readonly ShortInterestRepository _shortInterestRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public ShortDataTools(
        DailyShortVolumeRepository shortVolumeRepository,
        ShortInterestRepository shortInterestRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<ShortDataTools> logger
    )
    {
        _shortVolumeRepository = shortVolumeRepository;
        _shortInterestRepository = shortInterestRepository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetShortVolume")]
    [Description(
        "Get daily short volume data for a stock from FINRA. Shows short volume, exempt volume, total volume, and short volume percentage. High short volume % (>50%) may indicate bearish pressure."
    )]
    public Task<string> GetShortVolume(
        [Description("Stock ticker symbol (e.g., AAPL, GME, AMC)")] string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 3 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Maximum number of records to return (default: 90, newest first)")]
            int maxResults = 90
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var query = _shortVolumeRepository.GetHistoryByStock(stock);

                var (start, end) = McpToolExecutor.ParseDateRange(
                    startDate,
                    endDate,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3))
                );

                query = query.Where(d => d.Date >= start && d.Date <= end);

                var records = await query
                    .OrderByDescending(d => d.Date)
                    .Take(maxResults)
                    .ToListAsync();

                if (records.Count == 0)
                    return $"No short volume data found for {stock.Ticker} in the specified date range.";

                var result = MarkdownTable.Start(
                    $"Daily short volume for {stock.Ticker} ({stock.Name}):",
                    "| Date | Short Volume | Exempt | Total Volume | Short % |",
                    "|------|-------------|--------|-------------|---------|"
                );

                foreach (var r in records.OrderBy(r => r.Date))
                {
                    var shortPct =
                        r.TotalVolume > 0 ? (double)r.ShortVolume / r.TotalVolume * 100 : 0;
                    result.AppendLine(
                        $"| {r.Date:yyyy-MM-dd} | {r.ShortVolume:N0} | {r.ShortExemptVolume:N0} | {r.TotalVolume:N0} | {shortPct:F1}% |"
                    );
                }

                return result.ToString();
            },
            "GetShortVolume",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "GetShortInterest")]
    [Description(
        "Get short interest data for a stock from FINRA. Shows current short position, change from previous period, average daily volume, and days to cover. Published bi-monthly. High days-to-cover (>5) suggests a potential short squeeze."
    )]
    public Task<string> GetShortInterest(
        [Description("Stock ticker symbol (e.g., AAPL, GME, TSLA)")] string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Maximum number of records to return (default: 24, newest first)")]
            int maxResults = 24
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var query = _shortInterestRepository.GetHistoryByStock(stock);

                var (start, end) = McpToolExecutor.ParseDateRange(
                    startDate,
                    endDate,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1))
                );

                query = query.Where(s => s.SettlementDate >= start && s.SettlementDate <= end);

                var records = await query
                    .OrderByDescending(s => s.SettlementDate)
                    .Take(maxResults)
                    .ToListAsync();

                if (records.Count == 0)
                    return $"No short interest data found for {stock.Ticker} in the specified date range.";

                var result = MarkdownTable.Start(
                    $"Short interest for {stock.Ticker} ({stock.Name}):",
                    "| Settlement Date | Short Position | Change | Avg Daily Volume | Days to Cover |",
                    "|----------------|---------------|--------|-----------------|---------------|"
                );

                foreach (var r in records.OrderBy(r => r.SettlementDate))
                {
                    var changeStr = FormatSignedChange(r.ChangeInShortPosition);
                    var advStr = r.AverageDailyVolume?.ToString("N0") ?? "—";
                    var dtcStr = r.DaysToCover?.ToString("F1") ?? "—";
                    result.AppendLine(
                        $"| {r.SettlementDate:yyyy-MM-dd} | {r.CurrentShortPosition:N0} | {changeStr} | {advStr} | {dtcStr} |"
                    );
                }

                return result.ToString();
            },
            "GetShortInterest",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "GetShortInterestSnapshot")]
    [Description(
        "Get the latest short interest data across all stocks, sorted by days to cover (descending). Useful for finding stocks with high short interest that may be prone to short squeezes."
    )]
    public Task<string> GetShortInterestSnapshot(
        [Description("Minimum days to cover filter (default: 0)")] decimal minDaysToCover = 0,
        [Description("Maximum number of results to return (default: 50)")] int maxResults = 50
    )
    {
        return _runner.Execute(
            async () =>
            {
                var latestDate = await _shortInterestRepository
                    .GetLatestSettlementDate()
                    .FirstOrDefaultAsync();
                if (latestDate == default)
                    return "No short interest data available.";

                var query = _shortInterestRepository
                    .GetBySettlementDate(latestDate)
                    .Include(s => s.CommonStock)
                    .Where(s => s.DaysToCover != null)
                    .Where(s => s.AverageDailyVolume != null && s.AverageDailyVolume > 0);

                if (minDaysToCover > 0)
                {
                    query = query.Where(s => s.DaysToCover >= minDaysToCover);
                }

                var records = await query
                    .OrderByDescending(s => s.DaysToCover)
                    .Take(maxResults)
                    .ToListAsync();

                if (records.Count == 0)
                    return $"No short interest data found for settlement date {latestDate:yyyy-MM-dd} with days to cover >= {minDaysToCover}.";

                var result = MarkdownTable.Start(
                    $"Short interest snapshot — settlement date {latestDate:yyyy-MM-dd}:",
                    "| Ticker | Short Position | Change | Avg Daily Volume | Days to Cover |",
                    "|--------|---------------|--------|-----------------|---------------|"
                );

                foreach (var r in records)
                {
                    var changeStr = FormatSignedChange(r.ChangeInShortPosition);
                    var advStr = r.AverageDailyVolume?.ToString("N0") ?? "—";
                    result.AppendLine(
                        $"| {r.CommonStock.Ticker} | {r.CurrentShortPosition:N0} | {changeStr} | {advStr} | {r.DaysToCover:F1} |"
                    );
                }

                return result.ToString();
            },
            "GetShortInterestSnapshot",
            $"minDaysToCover: {minDaysToCover}"
        );
    }

    private static string FormatSignedChange(long change) =>
        change >= 0
            ? $"+{change.ToString("N0", CultureInfo.InvariantCulture)}"
            : change.ToString("N0", CultureInfo.InvariantCulture);

    // Thin forwarder so existing reflection-based normalization tests still find the method.
    private Task<(CommonStock Stock, string Error)> ResolveStockByTicker(string ticker) =>
        _commonStockRepository.ResolveByTicker(ticker);
}
