using System.ComponentModel;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Yahoo.Mcp.Tools;

[McpServerToolType]
public class StockPriceTools
{
    private readonly DailyStockPriceRepository _priceRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public StockPriceTools(
        DailyStockPriceRepository priceRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<StockPriceTools> logger
    )
    {
        _priceRepository = priceRepository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(
            logger,
            (tool, msg, stack, ctx) =>
                errorManager.Create(ErrorSource.McpTool, tool, msg, stack, ctx)
        );
    }

    [McpServerTool(Name = "GetStockPrices")]
    [Description(
        "Get daily OHLCV (Open, High, Low, Close, Volume) price history for a stock. Useful for technical analysis, charting, and price trend analysis."
    )]
    public Task<string> GetStockPrices(
        [Description("Stock ticker symbol (e.g., AAPL, MSFT, TSLA)")] string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Maximum number of records to return (default: 250, newest first)")]
            int maxResults = 250
    )
    {
        return _runner.Execute(
            async () =>
            {
                var stock = await FindStockByTicker(ticker);
                if (stock == null)
                    return McpToolExecutor.StockNotFound(ticker);

                var start = McpToolExecutor.ParseDateOr(
                    startDate,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1))
                );
                var end = McpToolExecutor.ParseDateOr(
                    endDate,
                    DateOnly.FromDateTime(DateTime.UtcNow)
                );

                var records = await _priceRepository
                    .GetByStock(stock, start, end)
                    .OrderByDescending(p => p.Date)
                    .Take(maxResults)
                    .ToListAsync();

                if (records.Count == 0)
                    return $"No price data found for {stock.Ticker} in the specified date range.";

                var result = StartTable(
                    $"Daily prices for {stock.Ticker} ({stock.Name}):",
                    "| Date | Open | High | Low | Close | Volume |",
                    "|------|------|------|-----|-------|--------|"
                );

                foreach (var p in records.OrderBy(p => p.Date))
                {
                    result.AppendLine(
                        $"| {p.Date:yyyy-MM-dd} | {p.Open:F2} | {p.High:F2} | {p.Low:F2} | {p.Close:F2} | {p.Volume:N0} |"
                    );
                }

                return result.ToString();
            },
            "GetStockPrices",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "GetLatestPrices")]
    [Description(
        "Get the most recent closing price and volume for one or more stocks. Useful for quick price checks across a portfolio or watchlist."
    )]
    public Task<string> GetLatestPrices(
        [Description("Comma-separated list of ticker symbols (e.g., 'AAPL,MSFT,GOOG,TSLA')")]
            string tickers
    )
    {
        return _runner.Execute(
            async () =>
            {
                var tickerList = tickers
                    .Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    )
                    .Select(t => t.ToUpperInvariant())
                    .Distinct()
                    .ToList();

                if (tickerList.Count == 0)
                    return "No tickers provided.";
                if (tickerList.Count > 25)
                    return "Maximum 25 tickers per request. Please split into multiple calls.";

                var result = StartTable(
                    "Latest prices:",
                    "| Ticker | Date | Close | Volume |",
                    "|--------|------|-------|--------|"
                );

                foreach (var ticker in tickerList)
                {
                    var stock = await _commonStockRepository.GetByTicker(ticker);
                    if (stock == null)
                    {
                        result.AppendLine($"| {ticker} | — | Not found | — |");
                        continue;
                    }

                    var latestDate = await _priceRepository
                        .GetLatestDate(stock)
                        .FirstOrDefaultAsync();
                    if (latestDate == default)
                    {
                        result.AppendLine($"| {ticker} | — | No data | — |");
                        continue;
                    }

                    var price = await _priceRepository
                        .GetByStock(stock, latestDate, latestDate)
                        .FirstOrDefaultAsync();
                    if (price == null)
                    {
                        result.AppendLine($"| {ticker} | — | No data | — |");
                        continue;
                    }

                    result.AppendLine(
                        $"| {ticker} | {price.Date:yyyy-MM-dd} | {price.Close:F2} | {price.Volume:N0} |"
                    );
                }

                return result.ToString();
            },
            "GetLatestPrices",
            $"tickers: {tickers}"
        );
    }

    [McpServerTool(Name = "GetStochasticOscillator")]
    [Description(
        "Stochastic Oscillator (%K and %D) for a stock. %K measures the close relative to "
            + "the high/low range over the lookback window; %D is the smoothed signal line "
            + "(simple moving average of %K). Useful for spotting overbought (>80) and "
            + "oversold (<20) conditions."
    )]
    public Task<string> GetStochasticOscillator(
        [Description("Stock ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 6 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Lookback window for %K (default: 14)")] int kPeriod = 14,
        [Description("Smoothing window for %D (default: 3)")] int dPeriod = 3,
        [Description("Maximum number of records to return (default: 60, newest first)")]
            int maxResults = 60
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (kPeriod < 2 || dPeriod < 1)
                    return "kPeriod must be at least 2 and dPeriod at least 1.";

                var (stock, records, error) = await LoadAscendingPriceWindow(
                    ticker,
                    startDate,
                    endDate
                );
                if (error != null)
                    return error;

                var (highs, lows, closes) = ExtractHighLowClose(records);
                var (k, d) = TechnicalIndicatorService.ComputeStochastic(
                    highs,
                    lows,
                    closes,
                    kPeriod,
                    dPeriod
                );

                var result = StartTable(
                    $"Stochastic Oscillator (%K={kPeriod}, %D={dPeriod}) for {stock.Ticker} ({stock.Name}):",
                    "| Date | Close | %K | %D |",
                    "|------|-------|----|----|"
                );

                AppendNewestFirstRows(
                    result,
                    records.Count,
                    maxResults,
                    i =>
                    {
                        var kCell = k[i].HasValue ? k[i].Value.ToString("F2") : "—";
                        var dCell = d[i].HasValue ? d[i].Value.ToString("F2") : "—";
                        return $"| {records[i].Date:yyyy-MM-dd} | {records[i].Close:F2} | {kCell} | {dCell} |";
                    }
                );

                return result.ToString();
            },
            "GetStochasticOscillator",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "GetAverageTrueRange")]
    [Description(
        "Average True Range (ATR) for a stock. Wilder's volatility measure built from "
            + "the True Range (max of high-low, |high-prev_close|, |low-prev_close|) and "
            + "smoothed recursively. Higher ATR means wider daily moves; commonly used "
            + "for position sizing and stop placement."
    )]
    public Task<string> GetAverageTrueRange(
        [Description("Stock ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 6 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Smoothing window (default: 14)")] int period = 14,
        [Description("Maximum number of records to return (default: 60, newest first)")]
            int maxResults = 60
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (period < 2)
                    return "period must be at least 2.";

                var (stock, records, error) = await LoadAscendingPriceWindow(
                    ticker,
                    startDate,
                    endDate
                );
                if (error != null)
                    return error;

                var (highs, lows, closes) = ExtractHighLowClose(records);
                var atr = TechnicalIndicatorService.ComputeAtr(highs, lows, closes, period);

                var result = StartTable(
                    $"Average True Range (period={period}) for {stock.Ticker} ({stock.Name}):",
                    "| Date | Close | ATR |",
                    "|------|-------|-----|"
                );

                AppendNewestFirstRows(
                    result,
                    records.Count,
                    maxResults,
                    i =>
                    {
                        var atrCell = atr[i].HasValue ? atr[i].Value.ToString("F4") : "—";
                        return $"| {records[i].Date:yyyy-MM-dd} | {records[i].Close:F2} | {atrCell} |";
                    }
                );

                return result.ToString();
            },
            "GetAverageTrueRange",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "GetOnBalanceVolume")]
    [Description(
        "On-Balance Volume (OBV) for a stock. Running cumulative volume that adds the "
            + "bar's volume on up-closes, subtracts on down-closes, and stays flat on "
            + "equal closes. Useful for confirming or diverging from price trends with "
            + "volume flow."
    )]
    public Task<string> GetOnBalanceVolume(
        [Description("Stock ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 6 months ago)")]
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
                var (stock, records, error) = await LoadAscendingPriceWindow(
                    ticker,
                    startDate,
                    endDate
                );
                if (error != null)
                    return error;

                var closes = records.Select(p => p.Close).ToList();
                var volumes = records.Select(p => p.Volume).ToList();
                var obv = TechnicalIndicatorService.ComputeObv(closes, volumes);

                var result = StartTable(
                    $"On-Balance Volume for {stock.Ticker} ({stock.Name}):",
                    "| Date | Close | Volume | OBV |",
                    "|------|-------|--------|-----|"
                );

                AppendNewestFirstRows(
                    result,
                    records.Count,
                    maxResults,
                    i =>
                        $"| {records[i].Date:yyyy-MM-dd} | {records[i].Close:F2} | {records[i].Volume:N0} | {obv[i]:N0} |"
                );

                return result.ToString();
            },
            "GetOnBalanceVolume",
            $"ticker: {ticker}"
        );
    }

    private async Task<(
        CommonStock Stock,
        List<DailyStockPrice> Records,
        string Error
    )> LoadAscendingPriceWindow(string ticker, string startDate, string endDate)
    {
        var stock = await FindStockByTicker(ticker);
        if (stock == null)
            return (null, null, McpToolExecutor.StockNotFound(ticker));

        var start = McpToolExecutor.ParseDateOr(
            startDate,
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6))
        );
        var end = McpToolExecutor.ParseDateOr(endDate, DateOnly.FromDateTime(DateTime.UtcNow));

        var records = await _priceRepository
            .GetByStock(stock, start, end)
            .OrderBy(p => p.Date)
            .ToListAsync();

        if (records.Count == 0)
            return (
                stock,
                null,
                $"No price data found for {stock.Ticker} in the specified date range."
            );

        return (stock, records, null);
    }

    private static StringBuilder StartTable(string title, string columnsRow, string separatorRow)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine();
        sb.AppendLine(columnsRow);
        sb.AppendLine(separatorRow);
        return sb;
    }

    private static void AppendNewestFirstRows(
        StringBuilder result,
        int count,
        int maxResults,
        Func<int, string> formatRow
    )
    {
        var emitted = 0;
        for (var i = count - 1; i >= 0 && emitted < maxResults; i--)
        {
            result.AppendLine(formatRow(i));
            emitted++;
        }
    }

    private static (
        List<decimal> Highs,
        List<decimal> Lows,
        List<decimal> Closes
    ) ExtractHighLowClose(List<DailyStockPrice> records) =>
        (
            records.Select(p => p.High).ToList(),
            records.Select(p => p.Low).ToList(),
            records.Select(p => p.Close).ToList()
        );

    private Task<CommonStock> FindStockByTicker(string ticker) =>
        _commonStockRepository.GetByTicker(ticker.Trim().ToUpperInvariant());
}
