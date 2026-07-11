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

                // Restate each day's volumes onto today's split basis so the series is
                // continuous across a split (a raw pre-split day would otherwise show a
                // phantom step against post-split days). The same-day Short % is a ratio of
                // two counts on the same date, so it is split-invariant and left as-is.
                var splits = await _stockSplitRepository.GetByStock(stock.Id).ToListAsync();

                return MarkdownTable.Render(
                    records.OrderBy(r => r.Date).ToList(),
                    $"No short volume data found for {stock.Ticker} in the specified date range.",
                    $"Daily short volume for {stock.Ticker} ({stock.Name}):",
                    "| Date | Short Volume | Exempt | Total Volume | Short % |",
                    "|------|-------------|--------|-------------|---------|",
                    r =>
                        RenderShortVolumeRow(
                            $"{r.Date:yyyy-MM-dd}",
                            r,
                            SplitAdjustment.ShareCountFactor(r.Date, splits)
                        )
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

                // Restate each settlement's share counts (short position, change, average
                // daily volume) onto today's split basis so the series is continuous across a
                // split. Days to cover is a same-settlement ratio (position ÷ ADV) and is left
                // as reported — restating the numerator and denominator by the same factor
                // leaves it unchanged anyway.
                var splits = await _stockSplitRepository.GetByStock(stock.Id).ToListAsync();

                return MarkdownTable.Render(
                    records.OrderBy(r => r.SettlementDate).ToList(),
                    $"No short interest data found for {stock.Ticker} in the specified date range.",
                    $"Short interest for {stock.Ticker} ({stock.Name}):",
                    "| Settlement Date | Short Position | Change | Avg Daily Volume | Days to Cover |",
                    "|----------------|---------------|--------|-----------------|---------------|",
                    r =>
                        RenderShortInterestRow(
                            $"{r.SettlementDate:yyyy-MM-dd}",
                            r,
                            SplitAdjustment.ShareCountFactor(r.SettlementDate, splits)
                        )
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
    private static string RenderShortInterestRow(
        string leadCell,
        ShortInterest r,
        decimal shareFactor = 1m
    )
    {
        var position = SplitAdjustment.AdjustShareCount(r.CurrentShortPosition, shareFactor);
        var changeStr = FormatSignedChange(
            SplitAdjustment.AdjustShareCount(r.ChangeInShortPosition, shareFactor)
        );
        // Average daily volume is a share count as-of the settlement; restate it too so the
        // row stays self-consistent (position ÷ ADV still reconciles to Days to Cover).
        var adv = r.AverageDailyVolume.HasValue
            ? SplitAdjustment.AdjustShareCount(r.AverageDailyVolume.Value, shareFactor)
            : (long?)null;
        var advStr = McpFormat.OrDash(adv, "N0");
        // Days to cover is a same-settlement ratio — left as reported (split-invariant).
        var dtcStr = McpFormat.OrDash(r.DaysToCover, "F1");
        return $"| {leadCell} | {McpFormat.WholeNumber(position)} | {changeStr} | {advStr} | {dtcStr} |";
    }

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
        if (score.HasPriceSpikeCatalyst && score.HasVolumeSurgeCatalyst)
            return "PriceSpike+VolumeSurge";
        if (score.HasPriceSpikeCatalyst)
            return "PriceSpike";
        if (score.HasVolumeSurgeCatalyst)
            return "VolumeSurge";
        return "-";
    }

    // Thin forwarder so existing reflection-based normalization tests still find the method.
    private Task<(CommonStock Stock, string Error)> ResolveStockByTicker(string ticker) =>
        _commonStockRepository.ResolveByTicker(ticker);

    [McpServerTool(Name = "GetShortSqueezeScores")]
    [Description(
        "Get the stocks with the highest composite short-squeeze score — a peer-relative 0-100 rank built as the weighted mean of six factor percentiles across every stock reporting short interest at the latest FINRA settlement date (short interest % of shares 30%, days to cover 20%, price vs trailing VWAP — how far shorts are underwater — 15%, short-volume trend 15%, change in short interest 10%, fails-to-deliver pressure 10%), plus catalyst boosts (+10 for a statistically extreme weekly price spike, +10 for abnormal dollar volume on a positive move, capped at +20, clamped to 100). Untradeable micro-caps dominate the raw board, so pass minMarketCap and/or minDollarVolume to keep only names that clear your liquidity bar (the score itself stays peer-relative to the full universe). Use this to find squeeze candidates; use GetShortInterest for one stock's underlying series."
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
