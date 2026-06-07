using System.ComponentModel;
using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Finra.Data.Models;
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
                    McpToolExecutor.UtcMonthsAgo(3)
                );

                query = query.Where(d => d.Date >= start && d.Date <= end);

                maxResults = McpLimit.Clamp(maxResults);

                var records = await query
                    .OrderByDescending(d => d.Date)
                    .Take(maxResults)
                    .ToListAsync();

                return MarkdownTable.Render(
                    records.OrderBy(r => r.Date).ToList(),
                    $"No short volume data found for {stock.Ticker} in the specified date range.",
                    $"Daily short volume for {stock.Ticker} ({stock.Name}):",
                    "| Date | Short Volume | Exempt | Total Volume | Short % |",
                    "|------|-------------|--------|-------------|---------|",
                    r => RenderShortVolumeRow($"{r.Date:yyyy-MM-dd}", r)
                );
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
                maxResults = McpLimit.Clamp(maxResults);

                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var query = _shortInterestRepository.GetHistoryByStock(stock);

                var (start, end) = McpToolExecutor.ParseDateRange(
                    startDate,
                    endDate,
                    McpToolExecutor.UtcYearsAgo(1)
                );

                query = query.Where(s => s.SettlementDate >= start && s.SettlementDate <= end);

                var records = await query
                    .OrderByDescending(s => s.SettlementDate)
                    .Take(maxResults)
                    .ToListAsync();

                return MarkdownTable.Render(
                    records.OrderBy(r => r.SettlementDate).ToList(),
                    $"No short interest data found for {stock.Ticker} in the specified date range.",
                    $"Short interest for {stock.Ticker} ({stock.Name}):",
                    "| Settlement Date | Short Position | Change | Avg Daily Volume | Days to Cover |",
                    "|----------------|---------------|--------|-----------------|---------------|",
                    r => RenderShortInterestRow($"{r.SettlementDate:yyyy-MM-dd}", r)
                );
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
                    .Take(McpLimit.Clamp(maxResults))
                    .ToListAsync();

                return MarkdownTable.Render(
                    records,
                    $"No short interest data found for settlement date {latestDate:yyyy-MM-dd} with days to cover >= {minDaysToCover.ToString(CultureInfo.InvariantCulture)}.",
                    $"Short interest snapshot — settlement date {latestDate:yyyy-MM-dd}:",
                    "| Ticker | Short Position | Change | Avg Daily Volume | Days to Cover |",
                    "|--------|---------------|--------|-----------------|---------------|",
                    r => RenderShortInterestRow(r.CommonStock.Ticker, r)
                );
            },
            "GetShortInterestSnapshot",
            $"minDaysToCover: {minDaysToCover}"
        );
    }

    [McpServerTool(Name = "GetLargestShortVolume")]
    [Description(
        "Get the stocks with the largest daily short volume for a single trading day (defaults to the latest available), sorted by short volume descending. Useful for spotting where short selling was most concentrated on a given day."
    )]
    public Task<string> GetLargestShortVolume(
        [Description("Trading day in YYYY-MM-DD format (defaults to the latest available day)")]
            string date = null,
        [Description("Minimum short volume filter (default: 0)")] long minShortVolume = 0,
        [Description("Maximum number of results to return (default: 50)")] int maxResults = 50
    )
    {
        return _runner.Execute(
            async () =>
            {
                var latestDate = await _shortVolumeRepository.GetLatestDate().FirstOrDefaultAsync();
                if (latestDate == default)
                    return "No short volume data available.";

                var tradingDay = McpToolExecutor.ParseDateOr(date, latestDate);

                var query = _shortVolumeRepository
                    .GetByDate(tradingDay)
                    .Include(d => d.CommonStock)
                    .Where(d => d.TotalVolume > 0);

                if (minShortVolume > 0)
                {
                    query = query.Where(d => d.ShortVolume >= minShortVolume);
                }

                var records = await query
                    .OrderByDescending(d => d.ShortVolume)
                    .Take(McpLimit.Clamp(maxResults))
                    .ToListAsync();

                return MarkdownTable.Render(
                    records,
                    $"No short volume data found for trading day {tradingDay:yyyy-MM-dd} with short volume >= {McpFormat.WholeNumber(minShortVolume)}.",
                    $"Largest short volume — trading day {tradingDay:yyyy-MM-dd}:",
                    "| Ticker | Short Volume | Exempt | Total Volume | Short % |",
                    "|--------|-------------|--------|-------------|---------|",
                    r => RenderShortVolumeRow(r.CommonStock.Ticker, r)
                );
            },
            "GetLargestShortVolume",
            $"date: {date}"
        );
    }

    // Render with InvariantCulture so the MCP markdown does not fork the separators by host
    // locale (e.g. de-DE would render 5.000.000 / 62,5%).
    private static string RenderShortVolumeRow(string leadCell, DailyShortVolume r)
    {
        var shortPct = r.TotalVolume > 0 ? (double)r.ShortVolume / r.TotalVolume * 100 : 0;
        return $"| {leadCell} | {McpFormat.WholeNumber(r.ShortVolume)} | {McpFormat.WholeNumber(r.ShortExemptVolume)} | {McpFormat.WholeNumber(r.TotalVolume)} | {McpFormat.Invariant(shortPct, "F1")}% |";
    }

    // Render with InvariantCulture so the MCP markdown does not fork the separators by host
    // locale (e.g. de-DE would render 1.234.567 / 12,3).
    private static string RenderShortInterestRow(string leadCell, ShortInterest r)
    {
        var changeStr = FormatSignedChange(r.ChangeInShortPosition);
        var advStr = McpFormat.OrDash(r.AverageDailyVolume, "N0");
        var dtcStr = McpFormat.OrDash(r.DaysToCover, "F1");
        return $"| {leadCell} | {McpFormat.WholeNumber(r.CurrentShortPosition)} | {changeStr} | {advStr} | {dtcStr} |";
    }

    private static string FormatSignedChange(long change) =>
        change >= 0 ? $"+{McpFormat.WholeNumber(change)}" : McpFormat.WholeNumber(change);

    // Thin forwarder so existing reflection-based normalization tests still find the method.
    private Task<(CommonStock Stock, string Error)> ResolveStockByTicker(string ticker) =>
        _commonStockRepository.ResolveByTicker(ticker);
}
