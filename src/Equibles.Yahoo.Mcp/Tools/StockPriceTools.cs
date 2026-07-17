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
        "Get daily OHLCV (Open, High, Low, Close, Volume) price history for a stock. Useful for "
            + "technical analysis, charting, and price trend analysis. Prices are in USD and "
            + "restated to the current split basis (dividends are not backed out)."
    )]
    public Task<string> GetStockPrices(
        [Description(
            "Stock ticker symbol (e.g., AAPL, MSFT, TSLA). Class shares use a dash (BRK-B); the dot form (BRK.B) is also accepted."
        )]
            string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description(
            "Maximum number of records to return (default: 250, max: 500). When the range holds more rows the newest are kept; rows are always listed oldest to newest."
        )]
            int maxResults = 250
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await ResolveTicker(ticker);
                if (stockError != null)
                    return stockError;

                var rangeError = ParseRangeStrict(
                    startDate,
                    endDate,
                    McpToolExecutor.UtcYearsAgo(1),
                    out var start,
                    out var end
                );
                if (rangeError != null)
                    return rangeError;

                maxResults = McpLimit.Clamp(maxResults);

                var rangeQuery = _priceRepository.GetByStock(stock, start, end);
                var total = await rangeQuery.CountAsync();

                var records = await rangeQuery
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

                result.AppendRows(
                    records.OrderBy(p => p.Date),
                    p =>
                        $"| {p.Date:yyyy-MM-dd} | {McpFormat.Invariant(p.Open, "F2")} | {McpFormat.Invariant(p.High, "F2")} | {McpFormat.Invariant(p.Low, "F2")} | {McpFormat.Invariant(p.Close, "F2")} | {McpFormat.WholeNumber(p.Volume)} |"
                );

                AppendNewestKeptTruncationNote(result, records.Count, total);

                return result.ToString();
            },
            "GetStockPrices",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "GetLatestPrices")]
    [Description(
        "Get the most recent closing price (USD), daily change, and volume for one or more "
            + "stocks. Useful for quick price checks across a portfolio or watchlist."
    )]
    public Task<string> GetLatestPrices(
        [Description(
            "Comma-separated list of ticker symbols (e.g., 'AAPL,MSFT,GOOG,TSLA'). Maximum 25 per request. Class shares use a dash (BRK-B); the dot form (BRK.B) is also accepted."
        )]
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
                    "| Ticker | Date | Close | Change | Change % | Volume |",
                    "|--------|------|-------|--------|----------|--------|"
                );

                foreach (var ticker in tickerList)
                {
                    var (stock, _) = await ResolveTicker(ticker);
                    if (stock == null)
                    {
                        result.AppendLine(PlaceholderRow(ticker, "Not found"));
                        continue;
                    }

                    // Newest two bars in one query: the latest row plus the prior close
                    // needed for the day-over-day change columns.
                    var latestTwo = await _priceRepository
                        .GetByStock(stock)
                        .OrderByDescending(p => p.Date)
                        .Take(2)
                        .ToListAsync();
                    if (latestTwo.Count == 0)
                    {
                        result.AppendLine(PlaceholderRow(ticker, "No data"));
                        continue;
                    }

                    var price = latestTwo[0];
                    var previousClose = latestTwo.Count > 1 ? latestTwo[1].Close : (decimal?)null;
                    var changeCell = "—";
                    var changePctCell = "—";
                    if (previousClose != null && previousClose.Value > 0)
                    {
                        var change = price.Close - previousClose.Value;
                        changeCell = McpFormat.Invariant(change, "+0.00;-0.00;0.00");
                        changePctCell =
                            McpFormat.Invariant(
                                change / previousClose.Value * 100m,
                                "+0.00;-0.00;0.00"
                            ) + "%";
                    }

                    result.AppendLine(
                        $"| {ticker} | {price.Date:yyyy-MM-dd} | {McpFormat.Invariant(price.Close, "F2")} | {changeCell} | {changePctCell} | {McpFormat.WholeNumber(price.Volume)} |"
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
            + "oversold (<20) conditions. The lookback window is warmed up on price history "
            + "fetched before startDate, so values do not depend on the requested range's left edge."
    )]
    public Task<string> GetStochasticOscillator(
        [Description(
            "Stock ticker symbol (e.g., AAPL, MSFT). Class shares use a dash (BRK-B); the dot form (BRK.B) is also accepted."
        )]
            string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 6 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Lookback window for %K (default: 14)")] int kPeriod = 14,
        [Description("Smoothing window for %D (default: 3)")] int dPeriod = 3,
        [Description(
            "Maximum number of records to return (default: 60, max: 500); the newest rows are kept and listed newest first."
        )]
            int maxResults = 60
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (kPeriod < 2 || dPeriod < 1)
                    return "kPeriod must be at least 2 and dPeriod at least 1.";

                maxResults = McpLimit.Clamp(maxResults);

                // %K needs kPeriod bars and %D another dPeriod - 1 %K values, so this many
                // extra bars before startDate make the first in-range row fully computable.
                var (stock, records, renderFrom, error) = await LoadAscendingPriceWindow(
                    ticker,
                    startDate,
                    endDate,
                    warmupBars: kPeriod + dPeriod - 2
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
                    renderFrom,
                    maxResults,
                    i =>
                    {
                        var kCell = McpFormat.OrDash(k[i], "F2");
                        var dCell = McpFormat.OrDash(d[i], "F2");
                        return $"| {DateAndCloseCells(records[i])} | {kCell} | {dCell} |";
                    },
                    "_'—' marks rows with too little prior price history to fill the lookback window._"
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
            + "for position sizing and stop placement. ATR is denominated in the stock's "
            + "price units (USD). The smoothing is warmed up on price history fetched before "
            + "startDate, so values do not depend on the requested range's left edge."
    )]
    public Task<string> GetAverageTrueRange(
        [Description(
            "Stock ticker symbol (e.g., AAPL, MSFT). Class shares use a dash (BRK-B); the dot form (BRK.B) is also accepted."
        )]
            string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 6 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Smoothing window (default: 14)")] int period = 14,
        [Description(
            "Maximum number of records to return (default: 60, max: 500); the newest rows are kept and listed newest first."
        )]
            int maxResults = 60
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (period < 2)
                    return "period must be at least 2.";

                maxResults = McpLimit.Clamp(maxResults);

                // Wilder smoothing is recursive, so the seed's influence decays rather than
                // ends: two extra periods of pre-range bars push the seed far enough back
                // that in-range values no longer depend on where the caller put startDate.
                var (stock, records, renderFrom, error) = await LoadAscendingPriceWindow(
                    ticker,
                    startDate,
                    endDate,
                    warmupBars: period * 2
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
                    renderFrom,
                    maxResults,
                    i =>
                    {
                        var atrCell = McpFormat.OrDash(atr[i], "F4");
                        return $"| {DateAndCloseCells(records[i])} | {atrCell} |";
                    },
                    "_ATR is in the stock's price units (USD). '—' marks rows with too little prior price history to fill the smoothing window._"
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
            + "volume flow. OBV is anchored at 0 on the first bar of the requested range, "
            + "so absolute values shift with startDate and are not comparable across calls "
            + "- read the slope and divergences, not the level."
    )]
    public Task<string> GetOnBalanceVolume(
        [Description(
            "Stock ticker symbol (e.g., AAPL, MSFT). Class shares use a dash (BRK-B); the dot form (BRK.B) is also accepted."
        )]
            string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 6 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description(
            "Maximum number of records to return (default: 60, max: 500); the newest rows are kept and listed newest first."
        )]
            int maxResults = 60
    )
    {
        return _runner.Execute(
            async () =>
            {
                maxResults = McpLimit.Clamp(maxResults);

                // No warm-up: OBV is a running sum with no lookback window, and the tool
                // contract deliberately anchors it at 0 on the range's first bar.
                var (stock, records, _, error) = await LoadAscendingPriceWindow(
                    ticker,
                    startDate,
                    endDate,
                    warmupBars: 0
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
                    renderFrom: 0,
                    maxResults,
                    i =>
                        $"| {DateAndCloseCells(records[i])} | {McpFormat.WholeNumber(records[i].Volume)} | {McpFormat.WholeNumber(obv[i])} |",
                    $"_OBV is anchored at 0 on {records[0].Date:yyyy-MM-dd} (the first bar of the requested range); absolute values shift with startDate, so compare slopes, not levels._"
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
            + "the upper/lower band is a common overbought/oversold cue. Includes %B "
            + "((close-lower)/(upper-lower)) and bandwidth ((upper-lower)/middle) columns. "
            + "The moving-average window is warmed up on price history fetched before "
            + "startDate, so values do not depend on the requested range's left edge."
    )]
    public Task<string> GetBollingerBands(
        [Description(
            "Stock ticker symbol (e.g., AAPL, MSFT). Class shares use a dash (BRK-B); the dot form (BRK.B) is also accepted."
        )]
            string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 6 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Moving-average window (default: 20)")] int period = 20,
        [Description("Standard deviations for the upper/lower bands (default: 2)")]
            decimal stdDev = 2m,
        [Description(
            "Maximum number of records to return (default: 60, max: 500); the newest rows are kept and listed newest first."
        )]
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

                maxResults = McpLimit.Clamp(maxResults);

                // The SMA window is exactly period bars, so period - 1 extra bars before
                // startDate make the first in-range row fully computable.
                var (stock, records, renderFrom, error) = await LoadAscendingPriceWindow(
                    ticker,
                    startDate,
                    endDate,
                    warmupBars: period - 1
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
                    "| Date | Close | Lower | Middle | Upper | %B | Bandwidth |",
                    "|------|-------|-------|--------|-------|----|-----------|",
                    records.Count,
                    renderFrom,
                    maxResults,
                    i =>
                    {
                        var lowerCell = McpFormat.OrDash(lower[i], "F2");
                        var middleCell = McpFormat.OrDash(middle[i], "F2");
                        var upperCell = McpFormat.OrDash(upper[i], "F2");
                        var percentB = PercentB(closes[i], upper[i], lower[i]);
                        var bandwidth = Bandwidth(middle[i], upper[i], lower[i]);
                        return $"| {DateAndCloseCells(records[i])} | {lowerCell} | {middleCell} | {upperCell} | {McpFormat.OrDash(percentB, "F2")} | {McpFormat.OrDash(bandwidth, "F4")} |";
                    },
                    "_'—' marks rows with too little prior price history to fill the moving-average window._"
                );
            },
            "GetBollingerBands",
            $"ticker: {ticker}"
        );
    }

    // %B = (close - lower) / (upper - lower). Null while the band window is still filling
    // or when the bands collapse to a zero-width range (flat prices).
    private static decimal? PercentB(decimal close, decimal? upper, decimal? lower)
    {
        if (upper == null || lower == null || upper == lower)
            return null;
        return Math.Round((close - lower.Value) / (upper.Value - lower.Value), 4);
    }

    // Bandwidth = (upper - lower) / middle. Null while the band window is still filling.
    private static decimal? Bandwidth(decimal? middle, decimal? upper, decimal? lower)
    {
        if (middle == null || middle.Value == 0 || upper == null || lower == null)
            return null;
        return Math.Round((upper.Value - lower.Value) / middle.Value, 4);
    }

    // Resolves a ticker to a stock, additionally accepting the dot class-share notation
    // (BRK.B) for the dash form the price data stores (BRK-B). This is a mechanical
    // format conversion between two spellings of the same symbol, not a heuristic.
    private async Task<(CommonStock Stock, string Error)> ResolveTicker(string ticker)
    {
        var (stock, error) = await _commonStockRepository.ResolveByTicker(ticker);
        if (stock == null && ticker != null && ticker.Contains('.'))
        {
            var (dashed, _) = await _commonStockRepository.ResolveByTicker(
                ticker.Replace('.', '-')
            );
            if (dashed != null)
                return (dashed, null);
        }
        return (stock, error);
    }

    // Strict argument parsing shared by the date-ranged price tools: a non-empty date must
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
    private static void AppendNewestKeptTruncationNote(StringBuilder result, int shown, int total)
    {
        if (shown >= total)
            return;
        result.AppendLine();
        result.AppendLine(
            $"_Showing the newest {shown} of {total} records in the range - raise maxResults or narrow the date range to see older rows._"
        );
    }

    private async Task<(
        CommonStock Stock,
        List<DailyStockPrice> Records,
        int RenderFrom,
        string Error
    )> LoadAscendingPriceWindow(string ticker, string startDate, string endDate, int warmupBars)
    {
        var (stock, stockError) = await ResolveTicker(ticker);
        if (stockError != null)
            return (null, null, 0, stockError);

        var rangeError = ParseRangeStrict(
            startDate,
            endDate,
            McpToolExecutor.UtcMonthsAgo(6),
            out var start,
            out var end
        );
        if (rangeError != null)
            return (stock, null, 0, rangeError);

        var records = await _priceRepository
            .GetByStock(stock, start, end)
            .OrderBy(p => p.Date)
            .ToListAsync();

        if (records.Count == 0)
            return (
                stock,
                null,
                0,
                $"No price data found for {stock.Ticker} in the specified date range."
            );

        // Warm-up look-back: indicators with a lookback window are computed over extra
        // bars fetched BEFORE the requested start so the early in-range rows aren't
        // null-padded warm-up dashes; only rows from RenderFrom onward are rendered.
        var renderFrom = 0;
        if (warmupBars > 0)
        {
            var warmup = await _priceRepository
                .GetByStock(stock)
                .Where(p => p.Date < start)
                .OrderByDescending(p => p.Date)
                .Take(warmupBars)
                .ToListAsync();
            if (warmup.Count > 0)
            {
                warmup.Reverse();
                records.InsertRange(0, warmup);
                renderFrom = warmup.Count;
            }
        }

        return (stock, records, renderFrom, null);
    }

    // Thin forwarder to the shared helper so the load-bearing title/blank/header/separator
    // sequence lives in one documented place; kept as a named method so existing
    // reflection-based pins still resolve it.
    private static StringBuilder StartTable(string title, string columnsRow, string separatorRow) =>
        MarkdownTable.Start(title, columnsRow, separatorRow);

    // Renders the newest-first indicator table shared by the technical-indicator tools:
    // the load-bearing title/header/separator start, the newest-first row loop from
    // renderFrom (warm-up rows before it stay hidden), a truncation note when maxResults
    // cut the renderable rows, then an optional footnote.
    private static string RenderNewestFirst(
        string title,
        string columnsRow,
        string separatorRow,
        int count,
        int renderFrom,
        int maxResults,
        Func<int, string> formatRow,
        string footnote = null
    )
    {
        var result = StartTable(title, columnsRow, separatorRow);
        AppendNewestFirstRows(result, count, renderFrom, maxResults, formatRow);

        var available = count - renderFrom;
        var note = McpOutput.TruncationNote(Math.Min(available, maxResults), available);
        if (note.Length > 0)
        {
            result.AppendLine();
            result.AppendLine(note);
        }

        if (footnote != null)
        {
            result.AppendLine();
            result.AppendLine(footnote);
        }

        return result.ToString();
    }

    private static void AppendNewestFirstRows(
        StringBuilder result,
        int count,
        int renderFrom,
        int maxResults,
        Func<int, string> formatRow
    )
    {
        var emitted = 0;
        for (var i = count - 1; i >= renderFrom && emitted < maxResults; i--)
        {
            result.AppendLine(formatRow(i));
            emitted++;
        }
    }

    // Placeholder row for a ticker with no price to show (unknown symbol or no data),
    // keeping the em-dash columns identical across the per-ticker fallback branches.
    private static string PlaceholderRow(string ticker, string status) =>
        $"| {ticker} | — | {status} | — | — | — |";

    // Leading "Date | Close" cells shared by every technical-indicator table row;
    // keeps the date format and close precision in sync across the four tables.
    private static string DateAndCloseCells(DailyStockPrice record) =>
        $"{record.Date:yyyy-MM-dd} | {McpFormat.Invariant(record.Close, "F2")}";

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
