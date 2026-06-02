using System.ComponentModel;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
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
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
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
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var (start, end) = McpToolExecutor.ParseDateRange(
                    startDate,
                    endDate,
                    McpToolExecutor.UtcYearsAgo(1)
                );

                maxResults = McpLimit.Clamp(maxResults);

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
                        $"| {p.Date:yyyy-MM-dd} | {McpFormat.Invariant(p.Open, "F2")} | {McpFormat.Invariant(p.High, "F2")} | {McpFormat.Invariant(p.Low, "F2")} | {McpFormat.Invariant(p.Close, "F2")} | {McpFormat.WholeNumber(p.Volume)} |"
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
                        $"| {ticker} | {price.Date:yyyy-MM-dd} | {McpFormat.Invariant(price.Close, "F2")} | {McpFormat.WholeNumber(price.Volume)} |"
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

                return RenderNewestFirst(
                    $"Stochastic Oscillator (%K={kPeriod}, %D={dPeriod}) for {stock.Ticker} ({stock.Name}):",
                    "| Date | Close | %K | %D |",
                    "|------|-------|----|----|",
                    records.Count,
                    maxResults,
                    i =>
                    {
                        var kCell = McpFormat.OrDash(k[i], "F2");
                        var dCell = McpFormat.OrDash(d[i], "F2");
                        return $"| {records[i].Date:yyyy-MM-dd} | {McpFormat.Invariant(records[i].Close, "F2")} | {kCell} | {dCell} |";
                    }
                );
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

                return RenderNewestFirst(
                    $"Average True Range (period={period}) for {stock.Ticker} ({stock.Name}):",
                    "| Date | Close | ATR |",
                    "|------|-------|-----|",
                    records.Count,
                    maxResults,
                    i =>
                    {
                        var atrCell = McpFormat.OrDash(atr[i], "F4");
                        return $"| {records[i].Date:yyyy-MM-dd} | {McpFormat.Invariant(records[i].Close, "F2")} | {atrCell} |";
                    }
                );
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

                return RenderNewestFirst(
                    $"On-Balance Volume for {stock.Ticker} ({stock.Name}):",
                    "| Date | Close | Volume | OBV |",
                    "|------|-------|--------|-----|",
                    records.Count,
                    maxResults,
                    i =>
                        $"| {records[i].Date:yyyy-MM-dd} | {McpFormat.Invariant(records[i].Close, "F2")} | {McpFormat.WholeNumber(records[i].Volume)} | {McpFormat.WholeNumber(obv[i])} |"
                );
            },
            "GetOnBalanceVolume",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "GetBollingerBands")]
    [Description(
        "Bollinger Bands for a stock. A middle band (simple moving average of close) with "
            + "upper and lower bands set a number of standard deviations above and below it. "
            + "Bands widen when volatility rises and contract when it falls; price touching "
            + "the upper/lower band is a common overbought/oversold cue."
    )]
    public Task<string> GetBollingerBands(
        [Description("Stock ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 6 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Moving-average window (default: 20)")] int period = 20,
        [Description("Standard deviations for the upper/lower bands (default: 2)")]
            decimal stdDev = 2m,
        [Description("Maximum number of records to return (default: 60, newest first)")]
            int maxResults = 60
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (period < 2)
                    return "period must be at least 2.";
                if (stdDev <= 0)
                    return "stdDev must be greater than 0.";

                var (stock, records, error) = await LoadAscendingPriceWindow(
                    ticker,
                    startDate,
                    endDate
                );
                if (error != null)
                    return error;

                var closes = records.Select(p => p.Close).ToList();
                var (middle, upper, lower) = TechnicalIndicatorService.ComputeBollingerBands(
                    closes,
                    period,
                    stdDev
                );

                return RenderNewestFirst(
                    $"Bollinger Bands (period={period}, stdDev={McpFormat.Invariant(stdDev, "0.#")}) for {stock.Ticker} ({stock.Name}):",
                    "| Date | Close | Lower | Middle | Upper |",
                    "|------|-------|-------|--------|-------|",
                    records.Count,
                    maxResults,
                    i =>
                    {
                        var lowerCell = McpFormat.OrDash(lower[i], "F2");
                        var middleCell = McpFormat.OrDash(middle[i], "F2");
                        var upperCell = McpFormat.OrDash(upper[i], "F2");
                        return $"| {records[i].Date:yyyy-MM-dd} | {McpFormat.Invariant(records[i].Close, "F2")} | {lowerCell} | {middleCell} | {upperCell} |";
                    }
                );
            },
            "GetBollingerBands",
            $"ticker: {ticker}"
        );
    }

    private async Task<(
        CommonStock Stock,
        List<DailyStockPrice> Records,
        string Error
    )> LoadAscendingPriceWindow(string ticker, string startDate, string endDate)
    {
        var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
        if (stockError != null)
            return (null, null, stockError);

        var (start, end) = McpToolExecutor.ParseDateRange(
            startDate,
            endDate,
            McpToolExecutor.UtcMonthsAgo(6)
        );

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

    // Thin forwarder to the shared helper so the load-bearing title/blank/header/separator
    // sequence lives in one documented place; kept as a named method so existing
    // reflection-based pins still resolve it.
    private static StringBuilder StartTable(string title, string columnsRow, string separatorRow) =>
        MarkdownTable.Start(title, columnsRow, separatorRow);

    // Renders the newest-first indicator table shared by the technical-indicator tools:
    // the load-bearing title/header/separator start, the newest-first row loop, then ToString.
    private static string RenderNewestFirst(
        string title,
        string columnsRow,
        string separatorRow,
        int count,
        int maxResults,
        Func<int, string> formatRow
    )
    {
        var result = StartTable(title, columnsRow, separatorRow);
        AppendNewestFirstRows(result, count, maxResults, formatRow);
        return result.ToString();
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
}
