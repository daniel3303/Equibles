using System.ComponentModel;
using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Finra.BusinessLogic;
using Equibles.Finra.BusinessLogic.Models;
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
    // FINRA reports days to cover capped at 999.99 — a stored 999.99 means "999.99 or
    // more", not a real reading. Rendered and ranked as a sentinel, never as a genuine
    // extreme value (an "F1" format would round it to a fictitious "1000.0").
    private const decimal FinraDaysToCoverCap = 999.99m;

    private readonly DailyShortVolumeRepository _shortVolumeRepository;
    private readonly ShortInterestRepository _shortInterestRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly ShortSqueezeScoreManager _shortSqueezeScoreManager;
    private readonly StockSplitRepository _stockSplitRepository;
    private readonly McpToolRunner _runner;

    public ShortDataTools(
        DailyShortVolumeRepository shortVolumeRepository,
        ShortInterestRepository shortInterestRepository,
        CommonStockRepository commonStockRepository,
        ShortSqueezeScoreManager shortSqueezeScoreManager,
        StockSplitRepository stockSplitRepository,
        ErrorManager errorManager,
        ILogger<ShortDataTools> logger
    )
    {
        _shortVolumeRepository = shortVolumeRepository;
        _shortInterestRepository = shortInterestRepository;
        _commonStockRepository = commonStockRepository;
        _shortSqueezeScoreManager = shortSqueezeScoreManager;
        _stockSplitRepository = stockSplitRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetShortVolume")]
    [Description(
        "Get daily short sale volume history for a stock from FINRA's short sale volume files. Shows short volume, short-exempt volume, total volume, and short volume percentage per trading day. Volumes cover trades reported to FINRA facilities (off-exchange/TRF) only — NOT consolidated tape volume — and a 40-50% Short % is the normal baseline from market-maker liquidity provision, so it must not be quoted as a share of the stock's total traded volume. This daily flow metric is distinct from bi-monthly short interest positions: use GetShortInterest for positions, GetLargestShortVolume for a market-wide single-day ranking, and GetShortSqueezeScores for squeeze candidates."
    )]
    public Task<string> GetShortVolume(
        [Description("Stock ticker symbol (e.g., AAPL, GME, AMC)")] string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 3 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description(
            "Maximum number of records to return — keeps the most recent N trading days in the range, displayed oldest to newest (default: 90, max: 500)"
        )]
            int maxResults = 90
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var (start, end, rangeError) = ParseStrictDateRange(
                    startDate,
                    endDate,
                    McpToolExecutor.UtcMonthsAgo(3)
                );
                if (rangeError != null)
                    return rangeError;

                var query = _shortVolumeRepository
                    .GetHistoryByStock(stock)
                    .Where(d => d.Date >= start && d.Date <= end);

                maxResults = McpLimit.Clamp(maxResults);

                var total = await query.CountAsync();
                var records = await query
                    .OrderByDescending(d => d.Date)
                    .Take(maxResults)
                    .ToListAsync();

                if (records.Count == 0)
                {
                    // Distinguish "range predates coverage" from a genuine per-stock gap —
                    // the full-universe FINRA daily files were only loaded from a fixed
                    // floor, so an older range would otherwise read as a false factual
                    // "this stock had no short volume" claim.
                    var earliest = await _shortVolumeRepository
                        .GetHistoryByStock(stock)
                        .OrderBy(d => d.Date)
                        .Select(d => (DateOnly?)d.Date)
                        .FirstOrDefaultAsync();
                    if (earliest != null && end < earliest.Value)
                        return $"No short volume data for {stock.Ticker} before {earliest:yyyy-MM-dd} — coverage starts on {earliest:yyyy-MM-dd}. Adjust the date range.";
                    return $"No short volume data found for {stock.Ticker} in the specified date range.";
                }

                // Restate each day's volumes onto today's split basis so the series is
                // continuous across a split (a raw pre-split day would otherwise show a
                // phantom step against post-split days). The same-day Short % is a ratio of
                // two counts on the same date, so it is split-invariant and left as-is.
                var splits = await _stockSplitRepository.GetByStock(stock.Id).ToListAsync();

                var table = MarkdownTable.Render(
                    records.OrderBy(r => r.Date).ToList(),
                    $"No short volume data found for {stock.Ticker} in the specified date range.",
                    $"Daily short volume for {stock.Ticker} ({stock.Name}):",
                    "_Volumes are trades reported to FINRA facilities (off-exchange/TRF) only — not consolidated tape volume; a 40-50% Short % is the normal baseline. Short Exempt = short sales exempt from Reg SHO price-test restrictions. Share counts are restated onto today's split basis._",
                    "| Date | Short Volume | Short Exempt | Total Volume | Short % |",
                    "|------|-------------|--------------|-------------|---------|",
                    r =>
                        RenderShortVolumeRow(
                            $"{r.Date:yyyy-MM-dd}",
                            r,
                            SplitAdjustment.ShareCountFactor(r.Date, splits)
                        )
                );

                return AppendNote(table, NewestKeptNote(records.Count, total, "trading days"));
            },
            "GetShortVolume",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "GetShortInterest")]
    [Description(
        "Get bi-monthly short interest history for a stock from FINRA. Shows the reported short position, change from the previous settlement, average daily volume, and days to cover per settlement date. Share counts are restated onto today's split basis so the series stays continuous across stock splits; days to cover is as reported (FINRA caps it at 999.99). High days-to-cover (>5) suggests a potential short squeeze — for short interest as a % of shares outstanding and an actual squeeze-candidate ranking use GetShortSqueezeScores; for the market-wide latest settlement use GetShortInterestSnapshot."
    )]
    public Task<string> GetShortInterest(
        [Description("Stock ticker symbol (e.g., AAPL, GME, TSLA)")] string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description(
            "Maximum number of records to return — keeps the most recent N settlements in the range, displayed oldest to newest (default: 24, max: 500)"
        )]
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

                var (start, end, rangeError) = ParseStrictDateRange(
                    startDate,
                    endDate,
                    McpToolExecutor.UtcYearsAgo(1)
                );
                if (rangeError != null)
                    return rangeError;

                var query = _shortInterestRepository
                    .GetHistoryByStock(stock)
                    .Where(s => s.SettlementDate >= start && s.SettlementDate <= end);

                var total = await query.CountAsync();

                // One extra settlement past the window so the oldest DISPLAYED row still has
                // its predecessor available for the split-consistent Change computation below.
                var fetched = await query
                    .OrderByDescending(s => s.SettlementDate)
                    .Take(maxResults + 1)
                    .ToListAsync();

                var display = fetched.Take(maxResults).OrderBy(r => r.SettlementDate).ToList();

                // Restate each settlement's share counts (short position, change, average
                // daily volume) onto today's split basis so the series is continuous across a
                // split. Days to cover is a same-settlement ratio (position ÷ ADV) and is left
                // as reported — restating the numerator and denominator by the same factor
                // leaves it unchanged anyway.
                var splits = await _stockSplitRepository.GetByStock(stock.Id).ToListAsync();

                // FINRA's raw change is (current − previous) where the previous position is on
                // the PREVIOUS settlement's split basis, so scaling it by the current factor
                // renders a cross-basis number at the settlement straddling a split. Whenever
                // the predecessor settlement is on file (and FINRA's change really references
                // it), display the difference of the two restated positions instead, so the
                // Change column reconciles row-to-row across a split boundary.
                var ascAll = fetched.OrderBy(r => r.SettlementDate).ToList();
                var changeById = new Dictionary<Guid, long>(ascAll.Count);
                for (var i = 0; i < ascAll.Count; i++)
                {
                    var cur = ascAll[i];
                    var factor = SplitAdjustment.ShareCountFactor(cur.SettlementDate, splits);
                    var prev = i > 0 ? ascAll[i - 1] : null;
                    changeById[cur.Id] =
                        prev != null && prev.CurrentShortPosition == cur.PreviousShortPosition
                            ? SplitAdjustment.AdjustShareCount(cur.CurrentShortPosition, factor)
                                - SplitAdjustment.AdjustShareCount(
                                    prev.CurrentShortPosition,
                                    SplitAdjustment.ShareCountFactor(prev.SettlementDate, splits)
                                )
                            : SplitAdjustment.AdjustShareCount(cur.ChangeInShortPosition, factor);
                }

                var table = MarkdownTable.Render(
                    display,
                    $"No short interest data found for {stock.Ticker} in the specified date range.",
                    $"Short interest for {stock.Ticker} ({stock.Name}):",
                    "_Share counts are restated onto today's split basis so the series is continuous across splits; Days to Cover is as reported by FINRA (capped at 999.99)._",
                    "| Settlement Date | Short Position | Change | Avg Daily Volume | Days to Cover |",
                    "|----------------|---------------|--------|-----------------|---------------|",
                    r =>
                        RenderShortInterestRow(
                            $"{r.SettlementDate:yyyy-MM-dd}",
                            r,
                            SplitAdjustment.ShareCountFactor(r.SettlementDate, splits),
                            changeById[r.Id]
                        )
                );

                return AppendNote(table, NewestKeptNote(display.Count, total, "settlements"));
            },
            "GetShortInterest",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "GetShortInterestSnapshot")]
    [Description(
        "Market-wide snapshot of the latest FINRA bi-monthly short interest settlement — one row per stock, sorted by days to cover (descending) by default. FINRA caps days to cover at 999.99: capped rows are a sentinel (almost always illiquid names with a tiny average-daily-volume denominator) and are ranked after real readings; pass minAvgDailyVolume (e.g. 100000) to drop illiquid names entirely. This is the raw FINRA snapshot — for genuine short-squeeze candidate ranking use GetShortSqueezeScores; for one stock's history use GetShortInterest; for daily short-sale flow use GetShortVolume/GetLargestShortVolume."
    )]
    public Task<string> GetShortInterestSnapshot(
        [Description("Minimum days to cover filter (default: 0)")] decimal minDaysToCover = 0,
        [Description("Maximum number of results to return (default: 50, max: 500)")]
            int maxResults = 50,
        [Description(
            "Minimum average daily share volume — set a floor (e.g. 100000) to drop illiquid names whose days-to-cover is inflated by a tiny volume denominator (default: 0 = no floor)"
        )]
            long minAvgDailyVolume = 0,
        [Description(
            "Sort key: daysToCover (default; FINRA-capped 999.99 sentinel rows ranked last), shortPosition, or change (largest increase in short position first)"
        )]
            string sortBy = "daysToCover"
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

                if (minAvgDailyVolume > 0)
                {
                    query = query.Where(s => s.AverageDailyVolume >= minAvgDailyVolume);
                }

                // Every ordering ends on the ticker so the ranking is deterministic — the
                // 999.99 cap tier alone holds ~180 tied rows, and an ORDER BY that ends on a
                // tie lets Postgres return a different "top" set on every call.
                var sortKey = string.IsNullOrWhiteSpace(sortBy) ? "daysToCover" : sortBy.Trim();
                IOrderedQueryable<ShortInterest> ordered;
                if (sortKey.Equals("daysToCover", StringComparison.OrdinalIgnoreCase))
                {
                    // Rows at FINRA's cap are a sentinel tier, not real readings — rank them
                    // after genuine values so the default view is not 100% illiquid capped
                    // names (which would bury every actionable row).
                    ordered = query
                        .OrderBy(s => s.DaysToCover >= FinraDaysToCoverCap ? 1 : 0)
                        .ThenByDescending(s => s.DaysToCover)
                        .ThenByDescending(s => s.CurrentShortPosition)
                        .ThenBy(s => s.CommonStock.Ticker);
                }
                else if (sortKey.Equals("shortPosition", StringComparison.OrdinalIgnoreCase))
                {
                    ordered = query
                        .OrderByDescending(s => s.CurrentShortPosition)
                        .ThenBy(s => s.CommonStock.Ticker);
                }
                else if (sortKey.Equals("change", StringComparison.OrdinalIgnoreCase))
                {
                    ordered = query
                        .OrderByDescending(s => s.ChangeInShortPosition)
                        .ThenByDescending(s => s.CurrentShortPosition)
                        .ThenBy(s => s.CommonStock.Ticker);
                }
                else
                {
                    return McpOutput.InvalidArgument(
                        "sortBy",
                        sortBy,
                        "daysToCover, shortPosition, change"
                    );
                }

                var total = await query.CountAsync();
                var records = await ordered.Take(McpLimit.Clamp(maxResults)).ToListAsync();

                var volumeClause =
                    minAvgDailyVolume > 0
                        ? $" and average daily volume >= {McpFormat.WholeNumber(minAvgDailyVolume)}"
                        : "";
                var table = MarkdownTable.Render(
                    records,
                    $"No short interest data found for settlement date {latestDate:yyyy-MM-dd} with days to cover >= {minDaysToCover.ToString(CultureInfo.InvariantCulture)}{volumeClause}.",
                    $"Short interest snapshot — settlement date {latestDate:yyyy-MM-dd} (sorted by {sortKey}, descending):",
                    "_FINRA caps Days to Cover at 999.99 — \">=999.99 (FINRA cap)\" rows are that sentinel (true value unknown, typically illiquid names) and rank after real readings in the default sort._",
                    "| Ticker | Short Position | Change | Avg Daily Volume | Days to Cover |",
                    "|--------|---------------|--------|-----------------|---------------|",
                    r => RenderShortInterestRow(r.CommonStock.Ticker, r)
                );

                return AppendNote(table, McpOutput.TruncationNote(records.Count, total));
            },
            "GetShortInterestSnapshot",
            $"minDaysToCover: {minDaysToCover}, minAvgDailyVolume: {minAvgDailyVolume}, sortBy: {sortBy}"
        );
    }

    [McpServerTool(Name = "GetLargestShortVolume")]
    [Description(
        "Get the stocks with the largest daily short sale volume for a single trading day (defaults to the latest available), from FINRA's daily short sale volume files, sorted by short volume descending. Short % is the share of that day's FINRA-facility (off-exchange/TRF) volume sold short — 40-50% is a normal market-making baseline — NOT short interest (the open short position; use GetShortInterest/GetShortInterestSnapshot for positions and GetShortSqueezeScores for squeeze candidates; use GetShortVolume for one stock's daily history). Pass sortBy=shortPercent with a minTotalVolume floor to rank by short intensity instead of raw size."
    )]
    public Task<string> GetLargestShortVolume(
        [Description("Trading day in YYYY-MM-DD format (defaults to the latest available day)")]
            string date = null,
        [Description("Minimum short volume filter (default: 0)")] long minShortVolume = 0,
        [Description("Maximum number of results to return (default: 50, max: 500)")]
            int maxResults = 50,
        [Description(
            "Sort key: shortVolume (default) or shortPercent — with shortPercent set a minTotalVolume floor, otherwise illiquid names dominate"
        )]
            string sortBy = "shortVolume",
        [Description(
            "Minimum total FINRA-reported volume filter, in shares (default: 0 = no floor)"
        )]
            long minTotalVolume = 0
    )
    {
        return _runner.Execute(
            async () =>
            {
                var latestDate = await _shortVolumeRepository.GetLatestDate().FirstOrDefaultAsync();
                if (latestDate == default)
                    return "No short volume data available.";

                var tradingDay = latestDate;
                if (!string.IsNullOrWhiteSpace(date))
                {
                    if (!McpOutput.TryParseDate(date, out var parsedDay))
                        return McpOutput.InvalidArgument("date", date, "yyyy-MM-dd");
                    tradingDay = DateOnly.FromDateTime(parsedDay);
                }

                var query = _shortVolumeRepository
                    .GetByDate(tradingDay)
                    .Include(d => d.CommonStock)
                    .Where(d => d.TotalVolume > 0);

                if (minShortVolume > 0)
                {
                    query = query.Where(d => d.ShortVolume >= minShortVolume);
                }

                if (minTotalVolume > 0)
                {
                    query = query.Where(d => d.TotalVolume >= minTotalVolume);
                }

                // Orderings end on the ticker so a re-call pages the same ranking even when
                // rows tie on the sort key.
                var sortKey = string.IsNullOrWhiteSpace(sortBy) ? "shortVolume" : sortBy.Trim();
                IOrderedQueryable<DailyShortVolume> ordered;
                if (sortKey.Equals("shortVolume", StringComparison.OrdinalIgnoreCase))
                {
                    ordered = query
                        .OrderByDescending(d => d.ShortVolume)
                        .ThenBy(d => d.CommonStock.Ticker);
                }
                else if (sortKey.Equals("shortPercent", StringComparison.OrdinalIgnoreCase))
                {
                    ordered = query
                        .OrderByDescending(d => (double)d.ShortVolume / d.TotalVolume)
                        .ThenByDescending(d => d.ShortVolume)
                        .ThenBy(d => d.CommonStock.Ticker);
                }
                else
                {
                    return McpOutput.InvalidArgument("sortBy", sortBy, "shortVolume, shortPercent");
                }

                var total = await query.CountAsync();
                var records = await ordered.Take(McpLimit.Clamp(maxResults)).ToListAsync();

                var totalVolumeClause =
                    minTotalVolume > 0
                        ? $" and total volume >= {McpFormat.WholeNumber(minTotalVolume)}"
                        : "";
                var table = MarkdownTable.Render(
                    records,
                    $"No short volume data found for trading day {tradingDay:yyyy-MM-dd} with short volume >= {McpFormat.WholeNumber(minShortVolume)}{totalVolumeClause}.",
                    $"Largest short volume — trading day {tradingDay:yyyy-MM-dd} (sorted by {sortKey}, descending):",
                    "_Volumes are trades reported to FINRA facilities (off-exchange/TRF) only — not consolidated tape volume; a 40-50% Short % is the normal baseline. Short Exempt = short sales exempt from Reg SHO price-test restrictions._",
                    "| Ticker | Company | Short Volume | Short Exempt | Total Volume | Short % |",
                    "|--------|---------|-------------|--------------|-------------|---------|",
                    // The lead "cell" carries both the ticker and company columns — the shared
                    // renderer splices it in front of the volume cells verbatim.
                    r => RenderShortVolumeRow($"{r.CommonStock.Ticker} | {r.CommonStock.Name}", r)
                );

                return AppendNote(table, McpOutput.TruncationNote(records.Count, total));
            },
            "GetLargestShortVolume",
            $"date: {date}, sortBy: {sortBy}, minTotalVolume: {minTotalVolume}"
        );
    }

    // Render with InvariantCulture so the MCP markdown does not fork the separators by host
    // locale (e.g. de-DE would render 5.000.000 / 62,5%). `shareFactor` restates the volume
    // counts onto today's split basis (1 = no adjustment, the single-date snapshot tools).
    private static string RenderShortVolumeRow(
        string leadCell,
        DailyShortVolume r,
        decimal shareFactor = 1m
    )
    {
        // Short % is computed from raw counts — it is a same-day ratio and split-invariant,
        // so the factor cancels; adjusting only the displayed absolute volumes.
        var shortPct = r.TotalVolume > 0 ? (double)r.ShortVolume / r.TotalVolume * 100 : 0;
        var shortVolume = SplitAdjustment.AdjustShareCount(r.ShortVolume, shareFactor);
        var exemptVolume = SplitAdjustment.AdjustShareCount(r.ShortExemptVolume, shareFactor);
        var totalVolume = SplitAdjustment.AdjustShareCount(r.TotalVolume, shareFactor);
        return $"| {leadCell} | {McpFormat.WholeNumber(shortVolume)} | {McpFormat.WholeNumber(exemptVolume)} | {McpFormat.WholeNumber(totalVolume)} | {McpFormat.Invariant(shortPct, "F1")}% |";
    }

    // Render with InvariantCulture so the MCP markdown does not fork the separators by host
    // locale (e.g. de-DE would render 1.234.567 / 12,3). `shareFactor` restates the share
    // counts onto today's split basis (1 = no adjustment, the single-date snapshot tools).
    // `changeOverride` replaces the default factor-scaled raw change with a value already
    // computed on a consistent basis (see GetShortInterest's split-boundary reconciliation).
    private static string RenderShortInterestRow(
        string leadCell,
        ShortInterest r,
        decimal shareFactor = 1m,
        long? changeOverride = null
    )
    {
        var position = SplitAdjustment.AdjustShareCount(r.CurrentShortPosition, shareFactor);
        var changeStr = FormatSignedChange(
            changeOverride ?? SplitAdjustment.AdjustShareCount(r.ChangeInShortPosition, shareFactor)
        );
        // Average daily volume is a share count as-of the settlement; restate it too so the
        // row stays self-consistent (position ÷ ADV still reconciles to Days to Cover).
        var adv = r.AverageDailyVolume.HasValue
            ? SplitAdjustment.AdjustShareCount(r.AverageDailyVolume.Value, shareFactor)
            : (long?)null;
        var advStr = McpFormat.OrDash(adv, "N0");
        // Days to cover is a same-settlement ratio — left as reported (split-invariant).
        // FINRA stores 999.99 as a cap sentinel meaning "999.99 or more"; render it
        // distinctly so an "F1" round-up never presents a fictitious "1000.0" as real data.
        var dtcStr =
            r.DaysToCover >= FinraDaysToCoverCap
                ? ">=999.99 (FINRA cap)"
                : McpFormat.OrDash(r.DaysToCover, "F1");
        return $"| {leadCell} | {McpFormat.WholeNumber(position)} | {changeStr} | {advStr} | {dtcStr} |";
    }

    // Strict replacement for McpToolExecutor.ParseDateRange: a supplied date must be ISO
    // yyyy-MM-dd (no silent fallback onto the default window) and the range must not be
    // inverted — a caller typo must never silently answer for a window it did not ask about,
    // and an inverted range must never masquerade as a factual "no data" claim.
    private static (DateOnly Start, DateOnly End, string Error) ParseStrictDateRange(
        string startDate,
        string endDate,
        DateOnly defaultStart
    )
    {
        var start = defaultStart;
        if (!string.IsNullOrWhiteSpace(startDate))
        {
            if (!McpOutput.TryParseDate(startDate, out var parsedStart))
                return (
                    default,
                    default,
                    McpOutput.InvalidArgument("startDate", startDate, "yyyy-MM-dd")
                );
            start = DateOnly.FromDateTime(parsedStart);
        }

        var end = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!string.IsNullOrWhiteSpace(endDate))
        {
            if (!McpOutput.TryParseDate(endDate, out var parsedEnd))
                return (
                    default,
                    default,
                    McpOutput.InvalidArgument("endDate", endDate, "yyyy-MM-dd")
                );
            end = DateOnly.FromDateTime(parsedEnd);
        }

        if (start > end)
            return (
                default,
                default,
                $"startDate {start:yyyy-MM-dd} is after endDate {end:yyyy-MM-dd} — startDate must be on or before endDate."
            );

        return (start, end, null);
    }

    // Sibling of McpOutput.TruncationNote for the history tools that KEEP the newest N rows
    // but DISPLAY them oldest-first: "first N" would read as the oldest N (the table appears
    // to start where the range starts), so the note names the kept end explicitly. Empty
    // when nothing was cut, so callers can append it unconditionally via AppendNote.
    private static string NewestKeptNote(int shown, int total, string unit) =>
        shown >= total
            ? string.Empty
            : $"_Showing the newest {shown} of {total} {unit} in the range — raise maxResults (max {McpLimit.MaxResults}) or narrow the date range to see earlier ones._";

    // Appends a note line under a rendered table (blank line first so strict CommonMark
    // renderers keep the table intact); a no-op for the empty note or an empty-state message.
    private static string AppendNote(string table, string note) =>
        note.Length == 0 ? table : $"{table}\n{note}\n";

    private static string FormatSignedChange(long change) =>
        change >= 0 ? $"+{McpFormat.WholeNumber(change)}" : McpFormat.WholeNumber(change);

    // Fractions rendered as explicit-sign percentages ("+3.1%"), dash when unknown.
    private static string FormatSignedPercent(decimal? fraction) =>
        fraction == null
            ? "-"
            : (fraction > 0 ? "+" : "")
                + fraction.Value.ToString("P1", CultureInfo.InvariantCulture);

    private static string FormatCatalysts(ShortSqueezeScore score)
    {
        var active = new List<string>(3);
        if (score.HasPriceSpikeCatalyst)
            active.Add("PriceSpike");
        if (score.HasVolumeSurgeCatalyst)
            active.Add("VolumeSurge");
        if (score.HasEarningsProximityCatalyst)
            active.Add("EarningsSoon");
        return active.Count == 0 ? "-" : string.Join("+", active);
    }

    // Thin forwarder so existing reflection-based normalization tests still find the method.
    private Task<(CommonStock Stock, string Error)> ResolveStockByTicker(string ticker) =>
        _commonStockRepository.ResolveByTicker(ticker);

    [McpServerTool(Name = "GetShortSqueezeScores")]
    [Description(
        "Get the stocks with the highest composite short-squeeze score — a peer-relative 0-100 rank built as the weighted mean of six factor percentiles across every stock reporting short interest at the latest FINRA settlement date (short interest % of shares 30%, days to cover 20%, price vs trailing VWAP — how far shorts are underwater — 15%, short-volume trend 15%, change in short interest 10%, fails-to-deliver pressure 10%), plus catalyst boosts (+10 for a statistically extreme weekly price spike, +10 for abnormal dollar volume on a positive move, +10 when a scheduled earnings event is within a few weekdays — squeezes cluster around earnings — capped at +20, clamped to 100). Untradeable micro-caps dominate the raw board, so pass minMarketCap and/or minDollarVolume to keep only names that clear your liquidity bar (the score itself stays peer-relative to the full universe). Use this to find squeeze candidates; use GetShortInterest for one stock's underlying series."
    )]
    public Task<string> GetShortSqueezeScores(
        [Description(
            "Minimum market capitalization in US dollars (e.g. 300000000 = $300M; default 0 = no floor). Stocks with an unknown market cap are excluded when set."
        )]
            double minMarketCap = 0,
        [Description(
            "Minimum average daily dollar volume in US dollars, approximated as the FINRA average daily share volume times the market-cap-implied share price (e.g. 5000000 = $5M/day; default 0 = no floor). Stocks with unknown volume or market cap are excluded when set."
        )]
            double minDollarVolume = 0,
        [Description("Maximum number of stocks to return (default: 25, highest score first)")]
            int maxResults = 25
    )
    {
        return _runner.Execute(
            async () =>
            {
                var scores = await _shortSqueezeScoreManager.Compute();
                if (scores.Count == 0)
                    return "No short-squeeze scores available — no short interest data on file.";

                var settlementDate = scores[0].SettlementDate;

                // Liquidity gates are a view over the scored universe, applied after the
                // peer-relative percentiles so a stock's score never depends on the
                // caller's filter. Null (unknown) liquidity fails an active gate.
                var filtered = scores;
                if (minMarketCap > 0)
                    filtered = filtered.Where(s => s.MarketCapitalization >= minMarketCap).ToList();
                if (minDollarVolume > 0)
                    filtered = filtered
                        .Where(s => s.AverageDailyDollarVolume >= minDollarVolume)
                        .ToList();
                if (filtered.Count == 0)
                    return $"No scored stocks clear the requested liquidity floor (of {scores.Count} scored at settlement {settlementDate:yyyy-MM-dd}). Lower minMarketCap/minDollarVolume.";

                var take = Math.Clamp(maxResults, 1, 200);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(
                    $"# Highest short-squeeze scores — settlement {settlementDate:yyyy-MM-dd}"
                );
                sb.AppendLine();
                sb.AppendLine(
                    "Score = weighted mean of the available factor percentiles (0-100, peer-relative) plus catalyst boosts (price spike / volume surge, capped at +20). Avg $ Volume is approximate (FINRA share volume × market-cap-implied price)."
                );
                sb.AppendLine();
                sb.AppendLine(
                    "| # | Ticker | Score | Short % of Shares | Days to Cover | Short-Volume Trend | Δ Short Interest | Worst FTD % | Price vs VWAP | Catalysts | Market Cap | Avg $ Volume |"
                );
                sb.AppendLine(
                    "|---|--------|-------|-------------------|---------------|--------------------|------------------|-------------|---------------|-----------|------------|--------------|"
                );
                sb.AppendNumberedRows(
                    filtered.Take(take).ToList(),
                    (rank, score) =>
                    {
                        return $"| {rank} | {score.Ticker} | {score.Score.ToString("0", CultureInfo.InvariantCulture)} | "
                            + $"{score.ShortInterestPercentOfShares.ToString("P1", CultureInfo.InvariantCulture)} | "
                            + $"{score.DaysToCover?.ToString("0.0", CultureInfo.InvariantCulture) ?? "-"} | "
                            + $"{FormatSignedPercent(score.ShortVolumeShareTrend)} | "
                            + $"{FormatSignedPercent(score.ShortInterestChangePercent)} | "
                            + $"{McpFormat.OrDash(score.FailsToDeliverPercentOfShares, "P2")} | "
                            + $"{FormatSignedPercent(score.PriceAboveVwap)} | {FormatCatalysts(score)} | "
                            + $"{McpFormat.CompactUsd(score.MarketCapitalization)} | {McpFormat.CompactUsd(score.AverageDailyDollarVolume)} |";
                    }
                );

                if (filtered.Count > take)
                    sb.AppendLine($"\n({filtered.Count - take} more scored stocks not shown.)");

                return sb.ToString();
            },
            "GetShortSqueezeScores",
            $"minMarketCap: {minMarketCap}, minDollarVolume: {minDollarVolume}, maxResults: {maxResults}"
        );
    }
}
