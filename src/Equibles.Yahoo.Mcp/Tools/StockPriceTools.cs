using System.ComponentModel;
using System.Text;
using Equibles.CommonStocks.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
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
    private readonly ErrorManager _errorManager;
    private readonly ILogger<StockPriceTools> _logger;

    public StockPriceTools(
        DailyStockPriceRepository priceRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<StockPriceTools> logger
    )
    {
        _priceRepository = priceRepository;
        _commonStockRepository = commonStockRepository;
        _errorManager = errorManager;
        _logger = logger;
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
        return McpToolExecutor.Execute(
            async () =>
            {
                var stock = await _commonStockRepository.GetByTicker(
                    ticker.Trim().ToUpperInvariant()
                );
                if (stock == null)
                    return $"Stock '{ticker}' not found.";

                var start =
                    !string.IsNullOrEmpty(startDate)
                    && DateOnly.TryParse(startDate, out var parsedStart)
                        ? parsedStart
                        : DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));

                var end =
                    !string.IsNullOrEmpty(endDate) && DateOnly.TryParse(endDate, out var parsedEnd)
                        ? parsedEnd
                        : DateOnly.FromDateTime(DateTime.UtcNow);

                var records = await _priceRepository
                    .GetByStock(stock, start, end)
                    .OrderByDescending(p => p.Date)
                    .Take(maxResults)
                    .ToListAsync();

                if (records.Count == 0)
                    return $"No price data found for {stock.Ticker} in the specified date range.";

                var result = new StringBuilder();
                result.AppendLine($"Daily prices for {stock.Ticker} ({stock.Name}):");
                result.AppendLine();
                result.AppendLine("| Date | Open | High | Low | Close | Volume |");
                result.AppendLine("|------|------|------|-----|-------|--------|");

                foreach (var p in records.OrderBy(p => p.Date))
                {
                    result.AppendLine(
                        $"| {p.Date:yyyy-MM-dd} | {p.Open:F2} | {p.High:F2} | {p.Low:F2} | {p.Close:F2} | {p.Volume:N0} |"
                    );
                }

                return result.ToString();
            },
            _logger,
            "GetStockPrices",
            $"ticker: {ticker}",
            ReportError
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
        return McpToolExecutor.Execute(
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

                var result = new StringBuilder();
                result.AppendLine("Latest prices:");
                result.AppendLine();
                result.AppendLine("| Ticker | Date | Close | Volume |");
                result.AppendLine("|--------|------|-------|--------|");

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
            _logger,
            "GetLatestPrices",
            $"tickers: {tickers}",
            ReportError
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
        return McpToolExecutor.Execute(
            async () =>
            {
                if (kPeriod < 2 || dPeriod < 1)
                    return "kPeriod must be at least 2 and dPeriod at least 1.";

                var stock = await _commonStockRepository.GetByTicker(
                    ticker.Trim().ToUpperInvariant()
                );
                if (stock == null)
                    return $"Stock '{ticker}' not found.";

                var start =
                    !string.IsNullOrEmpty(startDate)
                    && DateOnly.TryParse(startDate, out var parsedStart)
                        ? parsedStart
                        : DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6));

                var end =
                    !string.IsNullOrEmpty(endDate) && DateOnly.TryParse(endDate, out var parsedEnd)
                        ? parsedEnd
                        : DateOnly.FromDateTime(DateTime.UtcNow);

                var records = await _priceRepository
                    .GetByStock(stock, start, end)
                    .OrderBy(p => p.Date)
                    .ToListAsync();

                if (records.Count == 0)
                    return $"No price data found for {stock.Ticker} in the specified date range.";

                var highs = records.Select(p => p.High).ToList();
                var lows = records.Select(p => p.Low).ToList();
                var closes = records.Select(p => p.Close).ToList();
                var (k, d) = TechnicalIndicatorService.ComputeStochastic(
                    highs,
                    lows,
                    closes,
                    kPeriod,
                    dPeriod
                );

                var result = new StringBuilder();
                result.AppendLine(
                    $"Stochastic Oscillator (%K={kPeriod}, %D={dPeriod}) for {stock.Ticker} ({stock.Name}):"
                );
                result.AppendLine();
                result.AppendLine("| Date | Close | %K | %D |");
                result.AppendLine("|------|-------|----|----|");

                var emitted = 0;
                for (var i = records.Count - 1; i >= 0 && emitted < maxResults; i--)
                {
                    var kCell = k[i].HasValue ? k[i].Value.ToString("F2") : "—";
                    var dCell = d[i].HasValue ? d[i].Value.ToString("F2") : "—";
                    result.AppendLine(
                        $"| {records[i].Date:yyyy-MM-dd} | {records[i].Close:F2} | {kCell} | {dCell} |"
                    );
                    emitted++;
                }

                return result.ToString();
            },
            _logger,
            "GetStochasticOscillator",
            $"ticker: {ticker}",
            ReportError
        );
    }

    private Task ReportError(string toolName, string message, string stackTrace, string context)
    {
        return _errorManager.Create(ErrorSource.McpTool, toolName, message, stackTrace, context);
    }
}
