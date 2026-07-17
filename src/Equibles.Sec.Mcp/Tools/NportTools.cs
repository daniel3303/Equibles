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

    [McpServerTool(Name = "GetFundHoldings")]
    [Description(
        "Get the portfolio holdings of a registered investment company (mutual fund or ETF) from its most recent SEC Form NPORT-P monthly report. Returns the fund's series, reporting period and net assets, followed by its largest holdings — issuer name, CUSIP, position size, U.S.-dollar value and share of net assets, with the asset category (e.g. EC equity-common, DBT debt, DE derivative). Use this to see what a fund or ETF owns. Only registered funds file NPORT-P; operating companies will return no data."
    )]
    public Task<string> GetFundHoldings(
        [Description("Fund or ETF ticker symbol (e.g., SPY, VOO)")] string ticker,
        [Description("Maximum number of holdings to return, largest first (default: 20)")]
            int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var filing = await _nportRepository
                    .GetByStock(stock)
                    .Include(f => f.Holdings)
                    .OrderByDescending(f => f.FilingDate)
                    .FirstOrDefaultAsync();

                if (filing == null)
                    return $"No Form NPORT-P portfolio reports found for {ticker}.";

                var holdings = filing
                    .Holdings.OrderByDescending(h => h.ValueUsd)
                    .Take(maxResults)
                    .ToList();

                var result = MarkdownTable.Start(
                    $"Portfolio holdings for {filing.SeriesName ?? stock.Name} ({ticker}) — "
                        + $"reported {filing.ReportPeriodDate:yyyy-MM-dd}, net assets ${FormatAmount(filing.NetAssets)}, "
                        + $"{filing.Holdings.Count} total holdings, showing the largest {holdings.Count}:",
                    "| Holding | CUSIP | Balance | Units | Value (USD) | % Net Assets | Category | Country |",
                    "|---------|-------|---------|-------|-------------|--------------|----------|---------|"
                );

                result.AppendRows(
                    holdings,
                    h =>
                        $"| {h.Name ?? "-"} | {h.Cusip ?? "-"} | {FormatAmount(h.Balance)} | {h.Units ?? "-"} | ${FormatAmount(h.ValueUsd)} | {FormatPercent(h.PercentValue)} | {h.AssetCategory ?? "-"} | {h.InvestmentCountry ?? "-"} |"
                );

                return result.ToString();
            },
            "GetFundHoldings",
            $"ticker: {ticker}"
        );
    }

    // A series whose most recent NPORT-P is older than this has stopped filing (liquidated or
    // merged — the form is due 60 days after each fiscal quarter, so 18 months is generous
    // even across staggered fiscal calendars) and must not count as a CURRENT holder.
    private static readonly TimeSpan CurrentHolderRecencyFloor = TimeSpan.FromDays(548);

    [McpServerTool(Name = "GetFundsHoldingStock")]
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
            string registrantOrSeries = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                if (string.IsNullOrEmpty(stock.Cusip))
                    return $"No CUSIP is on record for {ticker}, so its fund ownership cannot be resolved from Form NPORT-P reports.";

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
                        ? $"No fund reports a position in {ticker} on its most recent Form NPORT-P."
                        : $"No fund matching '{registrantOrSeries}' reports a position in {ticker} on its most recent Form NPORT-P.";

                var positions = await currentPositions
                    .OrderByDescending(p => p.ValueUsd)
                    .Take(McpLimit.Clamp(maxResults))
                    .ToListAsync();

                var filterLabel = string.IsNullOrWhiteSpace(registrantOrSeries)
                    ? ""
                    : $" matching '{registrantOrSeries.Trim()}'";
                var result = MarkdownTable.Start(
                    $"Funds holding {stock.Name} ({ticker}) on each series' most recent Form NPORT-P — "
                        + $"{totalCount} current fund positions{filterLabel}, showing the largest {positions.Count}. "
                        + "Report dates differ per series (each fund's own fiscal quarter):",
                    "| Registrant | Series | Report Date | Balance | Units | Value (USD) | % Net Assets | Long/Short |",
                    "|------------|--------|-------------|---------|-------|-------------|--------------|------------|"
                );

                result.AppendRows(
                    positions,
                    p =>
                        $"| {p.RegistrantName ?? "-"} | {p.SeriesName ?? "-"} | {p.ReportPeriodDate:yyyy-MM-dd} | {FormatAmount(p.Balance)} | {FormatUnits(p.Units)} | ${FormatAmount(p.ValueUsd)} | {FormatPercent(p.PercentValue)} | {p.PayoffProfile ?? "-"} |"
                );

                return result.ToString();
            },
            "GetFundsHoldingStock",
            $"ticker: {ticker}"
        );
    }

    private static string FormatAmount(decimal value) => McpFormat.Invariant(value, "N2");

    private static string FormatPercent(decimal value) => McpFormat.Invariant(value, "N2") + "%";

    // Form NPORT-P unit-of-measure codes (Item C.7). The raw code ("NS") is opaque to an MCP
    // consumer exactly when the Balance column's meaning matters most (bonds, derivatives);
    // unknown codes pass through as filed.
    private static string FormatUnits(string units) =>
        units switch
        {
            "NS" => "Shares",
            "PA" => "Principal (par)",
            "NC" => "Contracts",
            "OU" => "Other",
            null => "-",
            _ => units,
        };
}
