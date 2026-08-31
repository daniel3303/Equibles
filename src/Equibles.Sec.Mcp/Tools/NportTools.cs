using System.ComponentModel;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Sec.Mcp.Tools;

[McpServerToolType]
public class NportTools
{
    private readonly NportFilingRepository _nportRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public NportTools(
        NportFilingRepository nportRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<NportTools> logger
    )
    {
        _nportRepository = nportRepository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    // A series whose most recent NPORT-P is older than this has stopped filing (liquidated or
    // merged — the form is due 60 days after each fiscal quarter, so 18 months is generous
    // even across staggered fiscal calendars) and must not count as a CURRENT holder.
    private static readonly TimeSpan CurrentHolderRecencyFloor = TimeSpan.FromDays(548);

    [McpServerTool(Name = "GetFundsHoldingStock", Title = "Funds Holding a Stock", ReadOnly = true)]
    [Description(
        "Get the registered investment companies (mutual funds and ETFs) holding a given stock, from SEC Form NPORT-P portfolio reports. The stock's CUSIP is matched against the holding rows on each fund series' most recent report (series that stopped filing more than 18 months ago are excluded), so an exited position never shows as current. Returns the fund's registrant and series, the reporting period, the position size, its U.S.-dollar value, its share of the fund's net assets and the payoff profile (Long/Short), largest positions first. Report dates differ per fund series (each files on its own fiscal quarter), so values are as of each row's report date and cross-row totals mix as-of dates. Use this to see which funds and ETFs own a stock and how concentrated each position is."
    )]
    public Task<string> GetFundsHoldingStock(
        [Description("Stock ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description(
            "Maximum number of fund positions to return, largest first (default: 20, clamped to 1-500)"
        )]
            int maxResults = 20,
        [Description(
            "Optional registrant or series name filter (case-insensitive contains, e.g. 'Vanguard') — reaches positions beyond the largest 500"
        )]
            string registrantOrSeries = null,
        [Description(
            "Number of matching fund positions to skip before returning rows (default: 0)"
        )]
            int offset = 0
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return MarkdownText(stockError);

                var safeTicker = MarkdownText(ticker);

                if (string.IsNullOrEmpty(stock.Cusip))
                    return $"No CUSIP is on record for {safeTicker}, so its fund ownership cannot be resolved from Form NPORT-P reports.";

                var recencyFloor = DateOnly.FromDateTime(
                    DateTime.UtcNow - CurrentHolderRecencyFloor
                );
                var currentPositions = _nportRepository
                    .GetHoldingsByStockCusip(stock)
                    .Join(
                        _nportRepository.GetLatestPerSeries(recencyFloor),
                        h => h.NportFilingId,
                        f => f.Id,
                        (h, f) =>
                            new
                            {
                                h.Id,
                                f.RegistrantName,
                                f.SeriesName,
                                f.ReportPeriodDate,
                                h.Balance,
                                h.Units,
                                h.ValueUsd,
                                h.PercentValue,
                                h.PayoffProfile,
                            }
                    );

                if (!string.IsNullOrWhiteSpace(registrantOrSeries))
                {
                    var pattern = LikePattern.Contains(registrantOrSeries.Trim());
                    currentPositions = currentPositions.Where(p =>
                        (
                            p.RegistrantName != null
                            && EF.Functions.ILike(p.RegistrantName, pattern, LikePattern.EscapeChar)
                        )
                        || (
                            p.SeriesName != null
                            && EF.Functions.ILike(p.SeriesName, pattern, LikePattern.EscapeChar)
                        )
                    );
                }

                var totalCount = await currentPositions.CountAsync();
                if (totalCount == 0)
                    return string.IsNullOrWhiteSpace(registrantOrSeries)
                        ? $"No current position in {safeTicker} matched the ingested latest Form NPORT-P reports. This is a dataset coverage result, not evidence that no fund reports one."
                        : $"No current position in {safeTicker} for a fund matching '{MarkdownText(registrantOrSeries)}' matched the ingested latest Form NPORT-P reports. This is a dataset coverage result, not evidence that no fund reports one.";

                offset = McpLimit.ClampOffset(offset);
                var positions = await currentPositions
                    .OrderByDescending(p => p.ValueUsd)
                    .ThenBy(p => p.Id)
                    .Skip(offset)
                    .Take(McpLimit.Clamp(maxResults))
                    .ToListAsync();
                if (positions.Count == 0 && offset > 0)
                    return $"No results at offset {offset} - only {totalCount} current fund positions match; lower offset.";

                var filterLabel = string.IsNullOrWhiteSpace(registrantOrSeries)
                    ? ""
                    : $" matching '{MarkdownText(registrantOrSeries)}'";
                var result = MarkdownTable.Start(
                    $"Funds holding {MarkdownText(stock.Name)} ({safeTicker}) on each series' most recent Form NPORT-P — "
                        + $"{totalCount} current fund positions{filterLabel}, showing rows "
                        + $"{offset + 1}-{offset + positions.Count} by value. "
                        + "Report dates differ per series (each fund's own fiscal quarter):",
                    "| Registrant | Series | Report Date | Balance | Units | Value (USD) | % Net Assets | Long/Short |",
                    "|------------|--------|-------------|---------|-------|-------------|--------------|------------|"
                );

                result.AppendRows(
                    positions,
                    p =>
                        $"| {MarkdownText(p.RegistrantName) ?? "-"} | {MarkdownText(p.SeriesName) ?? "-"} | {p.ReportPeriodDate:yyyy-MM-dd} | {FormatAmount(p.Balance)} | {MarkdownText(FundCodes.Unit(p.Units))} | ${FormatAmount(p.ValueUsd)} | {FormatPercent(p.PercentValue)} | {MarkdownText(p.PayoffProfile) ?? "-"} |"
                );

                var pagingNote = McpOutput.PagedTruncationNote(positions.Count, totalCount, offset);
                if (pagingNote.Length > 0)
                {
                    result.AppendLine();
                    result.AppendLine(pagingNote);
                }

                return result.ToString();
            },
            "GetFundsHoldingStock",
            $"ticker: {ticker}, registrantOrSeries: {registrantOrSeries}, offset: {offset}"
        );
    }

    private static string FormatAmount(decimal value) => McpFormat.Invariant(value, "N2");

    private static string FormatPercent(decimal value) => McpFormat.Invariant(value, "N2") + "%";

    private static string MarkdownText(string value) =>
        value == null ? null : MarkdownTable.EscapeCell(value).Trim();
}
