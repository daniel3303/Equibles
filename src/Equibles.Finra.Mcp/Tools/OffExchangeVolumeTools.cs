using System.ComponentModel;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Repositories;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Finra.Mcp.Tools;

[McpServerToolType]
public class OffExchangeVolumeTools
{
    private readonly OffExchangeVolumeRepository _offExchangeVolumeRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public OffExchangeVolumeTools(
        OffExchangeVolumeRepository offExchangeVolumeRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<OffExchangeVolumeTools> logger
    )
    {
        _offExchangeVolumeRepository = offExchangeVolumeRepository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(
        Name = "GetOffExchangeVolume",
        Title = "Off-Exchange (Dark Pool) Volume",
        ReadOnly = true
    )]
    [Description(
        "Get weekly off-exchange (dark pool / OTC) trading volume for a stock from the FINRA OTC/ATS Transparency data. "
            + "Each week shows ATS (alternative trading system / dark pool) volume and trade count, non-ATS OTC volume and trade count, "
            + "and the total off-exchange volume (ATS + non-ATS OTC). The FINRA file does not include consolidated tape volume, so the "
            + "off-exchange share of total market volume is not reported here; compute that share elsewhere against a consolidated-volume source. "
            + "FINRA publishes each week on a delay (2 weeks for Tier 1 NMS stocks, longer for other tiers), so the latest week lags today."
    )]
    public Task<string> GetOffExchangeVolume(
        [Description("Stock ticker symbol (e.g., AAPL, GME, TSLA)")] string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 6 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description(
            "Maximum number of weeks to return — keeps the most recent N weeks in the range, displayed oldest to newest (default: 26, max: 500)"
        )]
            int maxResults = 26
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var (startWeek, endWeek, rangeError) = ParseStrictDateRange(
                    startDate,
                    endDate,
                    McpToolExecutor.UtcMonthsAgo(6)
                );
                if (rangeError != null)
                    return rangeError;

                maxResults = McpLimit.Clamp(maxResults);

                var query = _offExchangeVolumeRepository
                    .GetHistoryByStock(stock)
                    .Where(d => d.WeekStartDate >= startWeek && d.WeekStartDate <= endWeek);

                var total = await query.CountAsync();
                var records = await query
                    .OrderByDescending(d => d.WeekStartDate)
                    .Take(maxResults)
                    .ToListAsync();

                var table = MarkdownTable.Render(
                    records.OrderBy(r => r.WeekStartDate).ToList(),
                    $"No off-exchange volume data found for {stock.Ticker} in the specified date range.",
                    $"Weekly off-exchange (dark pool / OTC) volume for {stock.Ticker} ({stock.Name}):",
                    "| Week Start | ATS Volume | ATS Trades | Non-ATS OTC Volume | Non-ATS OTC Trades | Total Off-Exchange Volume |",
                    "|------------|-----------|-----------|-------------------|-------------------|--------------------------|",
                    r => RenderOffExchangeRow($"{r.WeekStartDate:yyyy-MM-dd}", r)
                );

                return AppendNote(table, NewestKeptNote(records.Count, total, "weeks"));
            },
            "GetOffExchangeVolume",
            $"ticker: {ticker}"
        );
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

    // Sibling of McpOutput.TruncationNote for a table that KEEPS the newest N weeks but
    // DISPLAYS them oldest-first: "first N" would read as the oldest N, so the note names
    // the kept end explicitly. Empty when nothing was cut.
    private static string NewestKeptNote(int shown, int total, string unit) =>
        shown >= total
            ? string.Empty
            : $"_Showing the newest {shown} of {total} {unit} in the range — raise maxResults (max {McpLimit.MaxResults}) or narrow the date range to see earlier ones._";

    // Appends a note line under a rendered table (blank line first so strict CommonMark
    // renderers keep the table intact); a no-op for the empty note or an empty-state message.
    private static string AppendNote(string table, string note) =>
        note.Length == 0 ? table : $"{table}\n{note}\n";

    // Render with InvariantCulture so the MCP markdown does not fork the separators by host
    // locale (e.g. de-DE would render 5.000.000 instead of 5,000,000). Total off-exchange
    // volume is the sum of ATS and non-ATS OTC volume; the share of consolidated tape volume
    // is intentionally omitted because the FINRA file carries no consolidated total.
    private static string RenderOffExchangeRow(string leadCell, OffExchangeVolume r)
    {
        var totalOffExchangeVolume = r.AtsVolume + r.NonAtsOtcVolume;
        return $"| {leadCell} | {McpFormat.WholeNumber(r.AtsVolume)} | {McpFormat.WholeNumber(r.AtsTradeCount)} | {McpFormat.WholeNumber(r.NonAtsOtcVolume)} | {McpFormat.WholeNumber(r.NonAtsOtcTradeCount)} | {McpFormat.WholeNumber(totalOffExchangeVolume)} |";
    }
}
