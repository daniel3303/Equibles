using System.ComponentModel;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Sec.Mcp.Tools;

[McpServerToolType]
public class FailToDeliverTools
{
    private readonly FailToDeliverRepository _ftdRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public FailToDeliverTools(
        FailToDeliverRepository ftdRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<FailToDeliverTools> logger
    )
    {
        _ftdRepository = ftdRepository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetFailsToDeliver", Title = "Fails-to-Deliver Data", ReadOnly = true)]
    [Description(
        "Get fails-to-deliver (FTD) data for a stock from the SEC's twice-monthly FTD files. Quantity is the aggregate net fail-to-deliver position OUTSTANDING on each settlement date — a balance, not that day's new fails, so never sum Quantity across dates. Price is the previous trading day's closing price (SEC file convention, not a settlement price) and Value = Quantity × Price. Dates absent from the table had no reported fails; the SEC publishes each half-month batch with roughly a two-week lag, so the newest rows trail today. High or persistent FTD balances may indicate naked short selling or settlement issues."
    )]
    public Task<string> GetFailsToDeliver(
        [Description("Stock ticker symbol (e.g., AAPL, GME, AMC)")] string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 3 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description(
            "Maximum number of records to return — keeps the most recent N settlement dates in the range, displayed oldest to newest (default: 90, max: 500)"
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

                maxResults = McpLimit.Clamp(maxResults);

                var query = _ftdRepository
                    .GetByStock(stock)
                    .Where(f => f.SettlementDate >= start && f.SettlementDate <= end);

                var total = await query.CountAsync();
                var records = await query
                    .OrderByDescending(f => f.SettlementDate)
                    .Take(maxResults)
                    .ToListAsync();

                var table = MarkdownTable.Render(
                    records.OrderBy(f => f.SettlementDate).ToList(),
                    $"No FTD data found for {stock.Ticker} in the specified date range.",
                    $"Fails-to-deliver for {stock.Ticker} ({stock.Name}):",
                    "_Quantity is the outstanding FTD position on each settlement date (a balance — do not sum across dates); Prior Close is the previous trading day's closing price per SEC convention; Value = Quantity × Prior Close. Dates absent from the table had no reported fails; the SEC publishes in half-month batches with a ~2-week lag._",
                    "| Settlement Date | Quantity | Prior Close | Value |",
                    "|----------------|---------|-------------|-------|",
                    f =>
                    {
                        var value = f.Quantity * f.Price;
                        return $"| {f.SettlementDate:yyyy-MM-dd} | {McpFormat.WholeNumber(f.Quantity)} | ${McpFormat.Invariant(f.Price, "F2")} | ${McpFormat.WholeNumber(value)} |";
                    }
                );

                return AppendNote(table, NewestKeptNote(records.Count, total, "settlement dates"));
            },
            "GetFailsToDeliver",
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

    // Sibling of McpOutput.TruncationNote for a table that KEEPS the newest N rows but
    // DISPLAYS them oldest-first: "first N" would read as the oldest N (the table appears
    // to start where the range starts), so the note names the kept end explicitly. Empty
    // when nothing was cut.
    private static string NewestKeptNote(int shown, int total, string unit) =>
        shown >= total
            ? string.Empty
            : $"_Showing the newest {shown} of {total} {unit} in the range — raise maxResults (max {McpLimit.MaxResults}) or narrow the date range to see earlier ones._";

    // Appends a note line under a rendered table (blank line first so strict CommonMark
    // renderers keep the table intact); a no-op for the empty note or an empty-state message.
    private static string AppendNote(string table, string note) =>
        note.Length == 0 ? table : $"{table}\n{note}\n";
}
