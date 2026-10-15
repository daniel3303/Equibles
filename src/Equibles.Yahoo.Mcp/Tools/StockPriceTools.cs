using System.ComponentModel;
using System.Text;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.Calendars;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Mcp.Contracts;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Yahoo.Mcp.Tools;

[McpServerToolType]
public class StockPriceTools
{
    // A 52-week window whose oldest stored bar starts later than this many days past the
    // year boundary is a shorter listed history, not a full year — markets close for
    // holidays and weekends, but a window starting weeks late is a young listing.
    private const int BaselineSlackDays = 14;

    // The longest exchange closure a venue day change may span (Lunar New Year, Golden Week).
    private const int VenueMaxSessionGapDays = 12;

    private readonly EquityDailyStockPriceRepository _priceRepository;
    private readonly EquityIssuerRepository _commonStockRepository;
    private readonly StockSplitRepository _stockSplitRepository;
    private readonly McpToolRunner _runner;

    // Injected as a sequence so the dependency stays optional: a deployment that releases every
    // stored bar to every caller registers nothing and resolves an empty one.
    private readonly IDailyBarRelease _release;

    public StockPriceTools(
        EquityDailyStockPriceRepository priceRepository,
        EquityIssuerRepository commonStockRepository,
        StockSplitRepository stockSplitRepository,
        ErrorManager errorManager,
        ILogger<StockPriceTools> logger,
        IEnumerable<IDailyBarRelease> releases = null
    )
    {
        _priceRepository = priceRepository;
        _commonStockRepository = commonStockRepository;
        _stockSplitRepository = stockSplitRepository;
        _release = releases?.FirstOrDefault();
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetStockPrices", Title = "Daily Price History", ReadOnly = true)]
    [Description(
        "Get daily OHLCV (Open, High, Low, Close, Volume) price history for a stock. Useful for "
            + "technical analysis, charting, and price trend analysis. A US listing is quoted in "
            + "USD; a listing on another exchange (MIC:TICKER) is quoted in its own trading "
            + "currency, which the output names. An Adj "
            + "Close column shows the provider's split- and cash-dividend-adjusted close when it "
            + "differs from Close. Captured corporate-action changes trigger a full-history "
            + "refresh of the exact listed series, but the stored rows do not certify which split "
            + "basis the provider returned. Do not treat reconciliation status alone as proof that "
            + "a window is a consistent total-return series. Zero-volume carry-forward candles are "
            + "excluded because they do not establish a traded market price."
    )]
    public Task<string> GetStockPrices(
        [Description(
            "Stock ticker symbol (e.g., AAPL, MSFT, TSLA). Class shares use a dash (BRK-B); the dot form (BRK.B) is also accepted. "
                + "A listing on another exchange is MIC:TICKER (XPAR:AIR, XLON:BP, XHKG:00005)."
        )]
            string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description(
            "Maximum number of records to return (default: 260, max: 500). When the range holds more rows the newest are kept; rows are always listed oldest to newest."
        )]
        // 260 covers the default 1-year window: a year holds ~251 trading sessions, so the
        // old default of 250 silently dropped the oldest day(s) of its own default range.
        int maxResults = 260
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (listing, stockError) = await ResolveTicker(ticker);
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
                var holdNote = ClampToRelease(await GetHold(), ref end);

                var rangeQuery = Traded(listing, start, end);
                var total = await rangeQuery.CountAsync();

                var records = await rangeQuery
                    .OrderByDescending(p => p.Date)
                    .Take(maxResults)
                    .ToListAsync();

                if (records.Count == 0)
                    return WithHoldNote(
                        $"No price data found for {listing.Symbol} in the specified date range.",
                        holdNote
                    );

                // The provider-adjusted series is replaced in full after captured splits and cash
                // dividends. A differing column is useful evidence, but the provider response does
                // not certify one universal split basis across every raw window.
                var hasAdjustment = records.Any(p => p.AdjustedClose != p.Close);
                var listingTitle = ListingTitle(listing);

                var result = hasAdjustment
                    ? StartTable(
                        $"Daily prices for {listingTitle}:",
                        "| Date | Open | High | Low | Close | Adj Close | Volume |",
                        "|------|------|------|-----|-------|-----------|--------|"
                    )
                    : StartTable(
                        $"Daily prices for {listingTitle}:",
                        "| Date | Open | High | Low | Close | Volume |",
                        "|------|------|------|-----|-------|--------|"
                    );

                result.AppendRows(
                    records.OrderBy(p => p.Date),
                    p =>
                        hasAdjustment
                            ? $"| {p.Date:yyyy-MM-dd} | {McpFormat.Price(p.Open)} | {McpFormat.Price(p.High)} | {McpFormat.Price(p.Low)} | {McpFormat.Price(p.Close)} | {McpFormat.Price(p.AdjustedClose)} | {McpFormat.WholeNumber(p.Volume)} |"
                            : $"| {p.Date:yyyy-MM-dd} | {McpFormat.Price(p.Open)} | {McpFormat.Price(p.High)} | {McpFormat.Price(p.Low)} | {McpFormat.Price(p.Close)} | {McpFormat.WholeNumber(p.Volume)} |"
                );

                AppendNewestKeptTruncationNote(result, records.Count, total);

                result.AppendLine();
                result.AppendLine(
                    hasAdjustment
                        ? "_Adj Close is the provider-adjusted close. Captured splits and cash dividends trigger a full-history refresh, but stored rows do not certify which split basis the provider returned. Do not infer a consistent total-return window from reconciliation status alone._"
                        : "_Adj Close equals Close on every row shown. Captured splits and cash dividends trigger a full-history refresh, but equality does not prove that a split-spanning window uses one basis._"
                );

                return WithHoldNote(result.ToString(), holdNote);
            },
            "GetStockPrices",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(
        Name = "GetLatestClosingPrices",
        Title = "Latest Closing Prices",
        ReadOnly = true
    )]
    [Description(
        "Get each ticker's newest traded, settled daily close (USD for US listings; a MIC:TICKER listing on another exchange in its own trading currency, named in a note), with one-session change, volume, and trailing 52-week closing range. Rows can have different dates while a session settles; use the Date column. Change is omitted when the immediately prior trading session is absent; for a MIC:TICKER row it compares with the previous traded close, omitted when that close is more than 12 days older or a stored session in between shows no trades. Split-limited or partial 52-week ranges are marked in the response. This is settled history, not an intraday quote."
    )]
    public Task<string> GetLatestClosingPrices(
        [Description(
            "Ticker symbols (max 25). Class shares may use BRK-B or BRK.B; a listing on another exchange is MIC:TICKER (XPAR:AIR)."
        )]
            string[] tickers
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (tickers == null || tickers.Length == 0)
                    return "No tickers provided.";

                var segments = tickers.Select(ticker => ticker?.Trim()).ToList();
                if (segments.All(string.IsNullOrWhiteSpace))
                    return "No tickers provided.";
                if (segments.Count > 25)
                    return "Maximum 25 tickers per request. Please split into multiple calls.";

                var tickerList = new List<string>();
                var seenTickers = new HashSet<string>(StringComparer.Ordinal);
                // A dotted segment is looked up as typed, so a provider suffix (MC.PA) is not folded to MC-PA.
                var dottedSpelling = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var segment in segments)
                {
                    var normalizedTicker = EquityListingSymbol.TryParse(
                        segment,
                        out var venueCode,
                        out var venueTicker
                    )
                        ? EquityListingSymbol.Display(venueTicker, null, venueCode)
                        : TickerNormalizer.NormalizeDashListed(segment);
                    if (normalizedTicker == null)
                        return $"Invalid ticker '{segment}'. Use 1-32 ASCII letters, digits, dots, or dashes.";
                    if (seenTickers.Add(normalizedTicker))
                    {
                        tickerList.Add(normalizedTicker);
                        if (segment.Contains('.') && venueCode == null)
                            dottedSpelling[normalizedTicker] = TickerNormalizer.NormalizeListed(
                                segment
                            );
                    }
                }

                var result = StartTable(
                    "Latest prices:",
                    "| Ticker | Date | Close | Change | Change % | Volume | 52W High | 52W Low | Off High | Above Low |",
                    "|--------|------|-------|--------|----------|--------|----------|---------|----------|-----------|"
                );

                // Set when at least one ticker's change columns were withheld because its
                // stored series skips the prior session, so the footnote explains the em-dash
                // rather than leaving the caller to read it as missing coverage.
                var gapped = false;

                // Set when a ticker's 52-week window holds less than a full year of stored
                // history (a recent listing), so the starred range is explained.
                var shortWindow = false;

                // Set when a ticker's raw 52-week window crosses a captured split. Raw rows carry
                // no basis metadata, so bars before the latest split are withheld and starred.
                var splitLimitedWindow = false;

                // Sessions of the rendered rows: more than one means the newest session is
                // still settling for part of the batch, which earns its own footnote.
                var rowDates = new HashSet<DateOnly>();
                var latestByTicker = new Dictionary<string, LatestPriceSelection>();

                // Quote unit of each venue row, named in a note because the table has no currency column.
                var venueUnits = new Dictionary<string, string>(StringComparer.Ordinal);
                var placeholderByTicker = new Dictionary<string, string>();
                var hold = await GetHold();

                foreach (var ticker in tickerList)
                {
                    var (listing, _) = await ResolveTicker(
                        dottedSpelling.GetValueOrDefault(ticker) ?? ticker
                    );
                    if (listing == null)
                    {
                        placeholderByTicker[ticker] = "Not found";
                        continue;
                    }

                    // Newest two bars in one query: the latest row plus the prior close
                    // needed for the day-over-day change columns.
                    var latestTwo = await Released(Traded(listing), hold)
                        .OrderByDescending(p => p.Date)
                        .Take(2)
                        .ToListAsync();
                    if (latestTwo.Count == 0)
                    {
                        placeholderByTicker[ticker] = "No data";
                        continue;
                    }

                    latestByTicker[ticker] = new LatestPriceSelection(
                        listing,
                        latestTwo[0],
                        latestTwo.Count > 1 ? latestTwo[1] : null
                    );
                }

                // One split query for the whole batch. The price/range reads remain scoped to
                // exact listings, while split attribution is resolved in memory per listing.
                var batchSplits = new List<StockSplit>();
                if (latestByTicker.Count > 0)
                {
                    var stockIds = latestByTicker
                        .Values.Select(selection => selection.Listing.Stock.Id)
                        .Distinct()
                        .ToArray();
                    var earliestCutoff = latestByTicker.Values.Min(selection =>
                        selection.Price.Date.AddDays(-365)
                    );
                    var latestEnd = latestByTicker.Values.Max(selection => selection.Price.Date);
                    batchSplits = await _stockSplitRepository
                        .GetAll()
                        .Where(split =>
                            stockIds.Contains(split.EquityIssuerId)
                            && split.EffectiveDate > earliestCutoff
                            && split.EffectiveDate <= latestEnd
                        )
                        .ToListAsync();
                }

                var splitsByIssuer = new IssuerSplitIndex(batchSplits);
                foreach (var ticker in tickerList)
                {
                    if (!latestByTicker.TryGetValue(ticker, out var selection))
                    {
                        result.AppendLine(PlaceholderRow(ticker, placeholderByTicker[ticker]));
                        continue;
                    }

                    var listing = selection.Listing;
                    EquityDailyStockPrice price = selection.Price;
                    EquityDailyStockPrice previous = selection.Previous;

                    // Trailing 52-week close range, anchored on the row's own session so a
                    // stock that stopped trading doesn't fabricate a fresh range. A completed
                    // provider refresh cannot certify the basis of raw rows, so a captured split
                    // inside the requested year moves the comparison start to that split date.
                    var cutoff = price.Date.AddDays(-365);
                    var applicableSplits =
                        listing.Venue == null
                            ? PriceSeriesSplitScope.ForPriceComparison(
                                splitsByIssuer.For(listing.Stock.Id),
                                listing.PriceTicker
                            )
                            : PriceSeriesSplitScope.ForPriceComparison(
                                splitsByIssuer.For(listing.Stock.Id),
                                listing.Venue.Id
                            );
                    var rowSymbol = listing.Venue == null ? ticker : listing.Symbol;
                    if (listing.Venue != null)
                        venueUnits[rowSymbol] = QuoteUnit(listing.Venue);
                    var comparableWindow = ComparablePriceWindow.Resolve(
                        cutoff,
                        price.Date,
                        applicableSplits
                    );
                    var venueSessionSkipped =
                        listing.Venue != null
                        && previous != null
                        && (
                            price.Date.DayNumber - previous.Date.DayNumber > VenueMaxSessionGapDays
                            || await _priceRepository
                                .GetByListing(listing.Venue.Id)
                                .AnyAsync(p => p.Date > previous.Date && p.Date < price.Date)
                        );
                    var previousClose = venueSessionSkipped
                        ? null
                        : DayChangeBasis(
                            price,
                            previous,
                            comparableWindow.SplitBoundaryDate,
                            listing.Venue != null
                        );
                    var changeCell = "—";
                    var changePctCell = "—";
                    if (previousClose != null)
                    {
                        var change = price.Close - previousClose.Value;
                        changeCell = McpFormat.SignedPrice(change);
                        changePctCell =
                            McpFormat.Invariant(
                                change / previousClose.Value * 100m,
                                "+0.00;-0.00;0.00"
                            ) + "%";
                    }
                    else if (
                        previous != null
                        && listing.Venue == null
                        && !IsPriorSession(price, previous)
                    )
                    {
                        // Only a skipped session earns the footnote. A prior row with a
                        // non-positive close or one before a split boundary is blanked too, and
                        // saying "no row for the session before" about either would be wrong.
                        gapped = true;
                    }
                    var range = await Traded(listing)
                        .Where(p =>
                            p.Date >= comparableWindow.Start && p.Date <= price.Date && p.Close > 0
                        )
                        .GroupBy(p => 1)
                        .Select(g => new
                        {
                            High = g.Max(p => p.Close),
                            Low = g.Min(p => p.Close),
                            Oldest = g.Min(p => p.Date),
                        })
                        .FirstOrDefaultAsync();

                    var cells =
                        range == null
                            ? FiftyTwoWeekCells.Empty
                            : BuildFiftyTwoWeekCells(
                                price.Close,
                                range.High,
                                range.Low,
                                range.Oldest,
                                cutoff,
                                comparableWindow.IsSplitLimited
                            );
                    shortWindow |=
                        range != null
                        && range.Oldest > cutoff.AddDays(BaselineSlackDays)
                        && !comparableWindow.IsSplitLimited;
                    splitLimitedWindow |= comparableWindow.IsSplitLimited;

                    rowDates.Add(price.Date);
                    result.AppendLine(
                        $"| {rowSymbol} | {price.Date:yyyy-MM-dd} | {McpFormat.Price(price.Close)} | {changeCell} | {changePctCell} | {McpFormat.WholeNumber(price.Volume)} | {cells.High} | {cells.Low} | {cells.OffHigh} | {cells.AboveLow} |"
                    );
                }

                if (venueUnits.Count > 0)
                {
                    result.AppendLine();
                    result.AppendLine(
                        "Note: rows written MIC:TICKER are listings outside the US, quoted in their own "
                            + "trading currency: "
                            + string.Join(
                                "; ",
                                venueUnits.Select(unit => $"{unit.Key} in {unit.Value}")
                            )
                            + ". Their Change compares with the previous traded close, since each "
                            + "exchange keeps its own holidays, and is omitted when that close is more "
                            + "than 12 days older or a stored session in between shows no trades. Every "
                            + "other row is in USD."
                    );
                }

                if (gapped)
                {
                    result.AppendLine();
                    result.AppendLine(
                        "Note: a Change of \"—\" means the stored series has no row for the session "
                            + "before the date shown, so no one-day move can be stated. The close and "
                            + "volume on that row are still correct. Use GetStockPrices to see which "
                            + "sessions are present."
                    );
                }

                if (rowDates.Count > 1)
                {
                    // The settling explanation only holds when the spread is a single session;
                    // a wider spread means some series are simply behind (stale or delisted
                    // listings keep their last stored session) and blaming the nightly settle
                    // would tell the caller a years-old close is a fresh artifact.
                    var newest = rowDates.Max();
                    var oneSessionSpread =
                        venueUnits.Count == 0
                        && rowDates.Min() >= UsMarketCalendar.PreviousTradingDay(newest);
                    result.AppendLine();
                    result.AppendLine(
                        oneSessionSpread
                            ? "Note: rows span two adjacent sessions (see the Date column). Each "
                                + "ticker shows its newest SETTLED daily bar, and for a few hours "
                                + "after a US close some tickers still show the prior session while "
                                + "the fresh bar settles. Anchor any dated output on each row's "
                                + "Date, not on the newest date in the batch."
                            : "Note: rows span more than one session (see the Date column) — some "
                                + "stored series end earlier than others, so anchor any dated "
                                + "output on each row's Date, not on the newest date in the batch."
                    );
                }

                if (shortWindow)
                {
                    result.AppendLine();
                    result.AppendLine(
                        "Note: a 52-week value marked \\* covers a stored history shorter than 52 "
                            + "weeks (for example a recent listing), so it is the range since the "
                            + "history begins, not a full year."
                    );
                }

                if (splitLimitedWindow)
                {
                    result.AppendLine();
                    result.AppendLine(
                        "Note: a 52-week value marked \\* can begin at the latest recorded split "
                            + "inside the requested year. A Change of \"—\" can also mean its "
                            + "previous session falls before that split. Earlier raw bars are "
                            + "withheld because stored rows do not identify their split basis; "
                            + "the displayed figures compare only the post-split interval."
                    );
                }

                return WithHoldNote(result.ToString(), hold?.Note);
            },
            "GetLatestClosingPrices",
            $"tickers: {string.Join(",", tickers ?? [])}"
        );
    }

    public Task<string> GetLatestClosingPrices(string tickers) =>
        GetLatestClosingPrices(tickers?.Split(',', StringSplitOptions.TrimEntries));

    [McpServerTool(
        Name = "GetStochasticOscillator",
        Title = "Stochastic Oscillator",
        ReadOnly = true
    )]
    [Description(
        "Stochastic Oscillator (%K and %D) for a stock. %K measures the close relative to "
            + "the high/low range over the lookback window; %D is the smoothed signal line "
            + "(simple moving average of %K). Useful for spotting overbought (>80) and "
            + "oversold (<20) conditions. The lookback window is warmed up on price history "
            + "fetched before startDate, so values do not depend on the requested range's left edge."
    )]
    public Task<string> GetStochasticOscillator(
        [Description(
            "Stock ticker symbol (e.g., AAPL, MSFT). Class shares use a dash (BRK-B); the dot form (BRK.B) is also accepted. "
                + "A listing on another exchange is MIC:TICKER (XPAR:AIR)."
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
                var (listing, records, renderFrom, holdNote, error) =
                    await LoadAscendingPriceWindow(
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
                    $"Stochastic Oscillator (%K={kPeriod}, %D={dPeriod}) for {ListingTitle(listing)}:",
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
                    "_'—' marks rows with too little prior price history to fill the lookback window._",
                    holdNote
                );
            },
            "GetStochasticOscillator",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(
        Name = "GetAverageTrueRange",
        Title = "Average True Range (ATR)",
        ReadOnly = true
    )]
    [Description(
        "Average True Range (ATR) for a stock. Wilder's volatility measure built from "
            + "the True Range (max of high-low, |high-prev_close|, |low-prev_close|) and "
            + "smoothed recursively. Higher ATR means wider daily moves; commonly used "
            + "for position sizing and stop placement. ATR is denominated in the stock's "
            + "price units (USD for a US listing, the listing's own quote unit otherwise). The "
            + "smoothing is warmed up on price history fetched before "
            + "startDate, so values do not depend on the requested range's left edge."
    )]
    public Task<string> GetAverageTrueRange(
        [Description(
            "Stock ticker symbol (e.g., AAPL, MSFT). Class shares use a dash (BRK-B); the dot form (BRK.B) is also accepted. "
                + "A listing on another exchange is MIC:TICKER (XPAR:AIR)."
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
                var (listing, records, renderFrom, holdNote, error) =
                    await LoadAscendingPriceWindow(
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
                    $"Average True Range (period={period}) for {ListingTitle(listing)}:",
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
                    "_ATR is in the stock's price units (USD for a US listing, otherwise the unit named in the title). '—' marks rows with too little prior price history to fill the smoothing window._",
                    holdNote
                );
            },
            "GetAverageTrueRange",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "GetOnBalanceVolume", Title = "On-Balance Volume (OBV)", ReadOnly = true)]
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
            "Stock ticker symbol (e.g., AAPL, MSFT). Class shares use a dash (BRK-B); the dot form (BRK.B) is also accepted. "
                + "A listing on another exchange is MIC:TICKER (XPAR:AIR)."
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
                var (listing, records, _, holdNote, error) = await LoadAscendingPriceWindow(
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
                    $"On-Balance Volume for {ListingTitle(listing)}:",
                    "| Date | Close | Volume | OBV |",
                    "|------|-------|--------|-----|",
                    records.Count,
                    renderFrom: 0,
                    maxResults,
                    i =>
                        $"| {DateAndCloseCells(records[i])} | {McpFormat.WholeNumber(records[i].Volume)} | {McpFormat.WholeNumber(obv[i])} |",
                    $"_OBV is anchored at 0 on {records[0].Date:yyyy-MM-dd} (the first bar of the requested range); absolute values shift with startDate, so compare slopes, not levels._",
                    holdNote
                );
            },
            "GetOnBalanceVolume",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "GetBollingerBands", Title = "Bollinger Bands", ReadOnly = true)]
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
            "Stock ticker symbol (e.g., AAPL, MSFT). Class shares use a dash (BRK-B); the dot form (BRK.B) is also accepted. "
                + "A listing on another exchange is MIC:TICKER (XPAR:AIR)."
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
                var (listing, records, renderFrom, holdNote, error) =
                    await LoadAscendingPriceWindow(
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
                    $"Bollinger Bands (period={period}, stdDev={McpFormat.Invariant(stdDev, "0.#")}) for {ListingTitle(listing)}:",
                    "| Date | Close | Lower | Middle | Upper | %B | Bandwidth |",
                    "|------|-------|-------|--------|-------|----|-----------|",
                    records.Count,
                    renderFrom,
                    maxResults,
                    i =>
                    {
                        var lowerCell = McpFormat.PriceOrDash(lower[i]);
                        var middleCell = McpFormat.PriceOrDash(middle[i]);
                        var upperCell = McpFormat.PriceOrDash(upper[i]);
                        var percentB = PercentB(closes[i], upper[i], lower[i]);
                        var bandwidth = Bandwidth(middle[i], upper[i], lower[i]);
                        return $"| {DateAndCloseCells(records[i])} | {lowerCell} | {middleCell} | {upperCell} | {McpFormat.OrDash(percentB, "F2")} | {McpFormat.OrDash(bandwidth, "F4")} |";
                    },
                    "_'—' marks rows with too little prior price history to fill the moving-average window._",
                    holdNote
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

    // CommonStock.Name describes the primary SEC listing. Secondary symbols can identify a
    // different share class or fund series, and no authoritative per-listing name is stored.
    // A venue listing names its own issuer and the unit its prices are quoted in.
    private static string ListingTitle(PricedListing listing) =>
        listing.Venue != null
            ? $"{listing.Symbol} ({listing.Stock.Name}), quoted in {QuoteUnit(listing.Venue)}"
        : string.Equals(
            listing.Stock.Presentation.Listing.Ticker,
            listing.PriceTicker,
            StringComparison.OrdinalIgnoreCase
        )
            ? $"{listing.PriceTicker} ({listing.Stock.Name})"
        : listing.PriceTicker;

    // Stored venue prices stay in the unit the exchange quotes, so London reads in pence as it trades.
    private static string QuoteUnit(EquityListing venue) =>
        venue.TradingCurrency == null ? "the listing's local currency"
        : venue.QuoteUnitMultiplier is null or 1m ? venue.TradingCurrency
        : venue.TradingCurrency == "GBP" && venue.QuoteUnitMultiplier == 0.01m
            ? "GBX (pence; 100 GBX = 1 GBP)"
        : $"units of {McpFormat.Invariant(venue.QuoteUnitMultiplier.Value, "0.######")} {venue.TradingCurrency}";

    private IQueryable<EquityDailyStockPrice> Traded(PricedListing listing) =>
        listing.Venue == null
            ? _priceRepository.GetTradedByStock(listing.Stock, listing.PriceTicker)
            : _priceRepository.GetTradedByListing(listing.Venue.Id);

    private IQueryable<EquityDailyStockPrice> Traded(
        PricedListing listing,
        DateOnly start,
        DateOnly end
    ) => Traded(listing).Where(p => p.Date >= start && p.Date <= end);

    // Resolves a ticker to its exact listing, folding BRK.B to the stored BRK-B, so a secondary listing
    // never falls through to the filer's primary bars; MIC:TICKER, or MC.PA after a US miss, names a venue.
    private async Task<(PricedListing Listing, string Error)> ResolveTicker(string ticker)
    {
        if (!EquityListingSymbol.TryParse(ticker, out _, out _))
        {
            var resolved = await ResolvePricedSpelling(ticker);
            if (resolved.Stock == null && ticker != null && ticker.Contains('.'))
            {
                var dashed = await ResolvePricedSpelling(ticker.Replace('.', '-'));
                if (dashed.Stock != null)
                    resolved = dashed;
            }
            if (resolved.Stock != null || ticker == null || !ticker.Contains('.'))
                return resolved.Stock == null
                    ? (null, resolved.Error)
                    : (new PricedListing(resolved.Stock, resolved.PriceTicker, null), null);

            var suffixed = await _commonStockRepository.ResolveSuffixAfterUsMiss(
                ticker,
                resolved.UsError
            );
            return suffixed.Venue == null
                ? (null, resolved.Error)
                : (new PricedListing(suffixed.Stock, suffixed.Venue.Ticker, suffixed.Venue), null);
        }

        var (stock, venue, error) = await _commonStockRepository.ResolveListing(ticker);
        return venue == null
            ? (null, error)
            : (new PricedListing(stock, venue.Ticker, venue), null);
    }

    // UsError is the issuer lookup's own error, null when a US issuer owns the spelling, so a suffix never
    // answers for a ticker a US issuer holds.
    private async Task<(
        EquityIssuer Stock,
        string PriceTicker,
        string Error,
        string UsError
    )> ResolvePricedSpelling(string lookupTicker)
    {
        var (stock, error) = await _commonStockRepository.ResolveByTicker(lookupTicker);
        if (stock == null)
            return (null, null, error, error);

        var priceTicker = SecondaryTickerPolicy.ResolveListedTicker(stock, lookupTicker);
        return priceTicker == null
            ? (null, null, $"Stock '{lookupTicker}' not found.", null)
            : (stock, priceTicker, null, null);
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
    // the shared "Showing first N" wording would point at the wrong end of the table. At the
    // cap "raise maxResults" is impossible advice, so the note names the cap instead.
    private static void AppendNewestKeptTruncationNote(StringBuilder result, int shown, int total)
    {
        if (shown >= total)
            return;
        var advice =
            shown >= McpLimit.MaxResults
                ? $"maxResults is at its cap of {McpLimit.MaxResults}; narrow the date range to see older rows"
                : "raise maxResults or narrow the date range to see older rows";
        result.AppendLine();
        result.AppendLine(
            $"_Showing the newest {shown} of {total} records in the range - {advice}._"
        );
    }

    private async Task<(
        PricedListing Listing,
        List<EquityDailyStockPrice> Records,
        int RenderFrom,
        string HoldNote,
        string Error
    )> LoadAscendingPriceWindow(string ticker, string startDate, string endDate, int warmupBars)
    {
        var (listing, stockError) = await ResolveTicker(ticker);
        if (stockError != null)
            return (null, null, 0, null, stockError);

        var rangeError = ParseRangeStrict(
            startDate,
            endDate,
            McpToolExecutor.UtcMonthsAgo(6),
            out var start,
            out var end
        );
        if (rangeError != null)
            return (listing, null, 0, null, rangeError);

        var holdNote = ClampToRelease(await GetHold(), ref end);
        var records = await Traded(listing, start, end).OrderBy(p => p.Date).ToListAsync();

        if (records.Count == 0)
            return (
                listing,
                null,
                0,
                holdNote,
                WithHoldNote(
                    $"No price data found for {listing.Symbol} in the specified date range.",
                    holdNote
                )
            );

        // Warm-up look-back: indicators with a lookback window are computed over extra
        // bars fetched BEFORE the requested start so the early in-range rows aren't
        // null-padded warm-up dashes; only rows from RenderFrom onward are rendered.
        var renderFrom = 0;
        if (warmupBars > 0)
        {
            var warmup = await Traded(listing)
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

        return (listing, records, renderFrom, holdNote, null);
    }

    // The session withheld from the current caller, or null when every stored bar is released.
    private Task<DailyBarHold> GetHold() =>
        _release?.GetHold() ?? Task.FromResult<DailyBarHold>(null);

    private static IQueryable<EquityDailyStockPrice> Released(
        IQueryable<EquityDailyStockPrice> bars,
        DailyBarHold hold
    ) => hold == null ? bars : bars.Where(p => p.Date < hold.Session);

    // Ends a range before the withheld session and returns the note the caller is owed, or
    // null when the range never reached that session.
    private static string ClampToRelease(DailyBarHold hold, ref DateOnly end)
    {
        if (hold == null || end < hold.Session)
            return null;
        end = hold.Session.AddDays(-1);
        return hold.Note;
    }

    private static string WithHoldNote(string output, string holdNote) =>
        holdNote == null
            ? output
            : $"{output.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{holdNote}{Environment.NewLine}";

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
        string footnote = null,
        string holdNote = null
    )
    {
        var result = StartTable(title, columnsRow, separatorRow);
        AppendNewestFirstRows(result, count, renderFrom, maxResults, formatRow);

        var available = count - renderFrom;
        var shown = Math.Min(available, maxResults);
        if (shown < available)
        {
            // These tables render newest-first, so name the kept end explicitly; and when
            // the shown count already sits AT the cap, "raise maxResults" is impossible
            // advice — the date range is this family's real continuation.
            var advice =
                shown >= McpLimit.MaxResults
                    ? $"maxResults is at its cap of {McpLimit.MaxResults}; narrow the date range to see older rows"
                    : "raise maxResults or narrow the date range to see older rows";
            result.AppendLine();
            result.AppendLine(
                $"_Showing the newest {shown} of {available} records in the range - {advice}._"
            );
        }

        if (footnote != null)
        {
            result.AppendLine();
            result.AppendLine(footnote);
        }

        return WithHoldNote(result.ToString(), holdNote);
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

    // The close a day change is measured FROM, or null when there is none.
    //
    // "The second-newest stored row" is NOT that close. The end-of-day price lane crawls the
    // whole common-stock universe and can finish a session or more behind, so on any given day
    // a slice of the universe is missing its most recent bar and the row below it is two or
    // more sessions back. Differencing that states a multi-session move as the day's move —
    // the close is right, the percentage is inflated, and nothing says so. Thinly traded
    // symbols make it extreme: a row 25 sessions apart from its predecessor reported a
    // five-figure percentage as a day change.
    //
    // So the basis is chosen by DATE, never by position: only the row dated the trading day
    // immediately before the latest one qualifies. Otherwise there is no day change to state,
    // and an absent percentage is honest where a wrong one is not.
    // A venue keeps its own holiday calendar, which the US one cannot judge, so its basis is the previous stored close.
    private static decimal? DayChangeBasis(
        EquityDailyStockPrice latest,
        EquityDailyStockPrice previous,
        DateOnly? splitBoundaryDate,
        bool venue
    ) =>
        (venue ? previous != null && previous.Date < latest.Date : IsPriorSession(latest, previous))
        && previous.Close > 0
        && (splitBoundaryDate == null || previous.Date >= splitBoundaryDate)
            ? previous.Close
            : null;

    // Whether the second-newest stored row is the session immediately before the newest one.
    // Shared with the caller so the "series skips a session" footnote and the decision to blank
    // the columns can never disagree.
    //
    // The test is "no NYSE session was skipped", NOT "the prior row is exactly the NYSE previous
    // session". Every date strictly between the previous NYSE session and the latest one is a
    // weekend or an NYSE holiday, so a bar there belongs to a security trading on some other
    // calendar — foreign ordinaries quoted here keep trading through Juneteenth, Good Friday and
    // Memorial Day, and 121-297 of them carry a bar on each. Demanding an exact match blanked a
    // correct one-session move for every one of them on the day after an NYSE holiday.
    private static bool IsPriorSession(
        EquityDailyStockPrice latest,
        EquityDailyStockPrice previous
    ) =>
        previous != null
        && previous.Date < latest.Date
        && previous.Date >= UsMarketCalendar.PreviousTradingDay(latest.Date);

    // The four rendered 52-week cells for one row, plus whether the range earned the
    // short-history star so the caller can emit the explaining footnote exactly once.
    private sealed record FiftyTwoWeekCells(
        string High,
        string Low,
        string OffHigh,
        string AboveLow,
        bool Starred
    )
    {
        public static readonly FiftyTwoWeekCells Empty = new("—", "—", "—", "—", false);
    }

    // Renders the trailing 52-week range cells from the window aggregate. The window is
    // anchored on the row's own session (cutoff = row date minus 365 days); a window whose
    // oldest bar starts more than BaselineSlackDays past the cutoff covers a shorter listed
    // history than a year, so its absolute values carry a star rather than being withheld.
    private static FiftyTwoWeekCells BuildFiftyTwoWeekCells(
        decimal close,
        decimal high,
        decimal low,
        DateOnly oldest,
        DateOnly cutoff,
        bool splitLimited = false
    )
    {
        var starred = splitLimited || oldest > cutoff.AddDays(BaselineSlackDays);
        var star = starred ? "\\*" : "";
        // The row's own close must be positive too: a corrupt $0 bar would otherwise render
        // both distances as -100%, breaking the ≤0 / ≥0 sign contract the columns promise.
        return new FiftyTwoWeekCells(
            McpFormat.Price(high) + star,
            McpFormat.Price(low) + star,
            high > 0 && close > 0
                ? McpFormat.Invariant((close / high - 1m) * 100m, "+0.00;-0.00;0.00") + "%"
                : "—",
            low > 0 && close > 0
                ? McpFormat.Invariant((close / low - 1m) * 100m, "+0.00;-0.00;0.00") + "%"
                : "—",
            starred
        );
    }

    private sealed record LatestPriceSelection(
        PricedListing Listing,
        EquityDailyStockPrice Price,
        EquityDailyStockPrice Previous
    );

    // The exact series a price tool reads: a US filer's listed symbol, or one venue listing.
    private sealed record PricedListing(EquityIssuer Stock, string PriceTicker, EquityListing Venue)
    {
        public string Symbol => Venue == null ? PriceTicker : EquityListingSymbol.Display(Venue);
    }

    // Placeholder row for a ticker with no price to show (unknown symbol or no data),
    // keeping the em-dash columns identical across the per-ticker fallback branches.
    private static string PlaceholderRow(string ticker, string status) =>
        $"| {ticker} | — | {status} | — | — | — | — | — | — | — |";

    // Leading "Date | Close" cells shared by every technical-indicator table row;
    // keeps the date format and close precision in sync across the four tables.
    private static string DateAndCloseCells(EquityDailyStockPrice record) =>
        $"{record.Date:yyyy-MM-dd} | {McpFormat.Price(record.Close)}";

    private static (
        List<decimal> Highs,
        List<decimal> Lows,
        List<decimal> Closes
    ) ExtractHighLowClose(List<EquityDailyStockPrice> records) =>
        (
            records.Select(p => p.High).ToList(),
            records.Select(p => p.Low).ToList(),
            records.Select(p => p.Close).ToList()
        );
}
