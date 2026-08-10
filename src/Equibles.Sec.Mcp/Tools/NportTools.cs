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
    private readonly FundSeriesRepository _fundSeriesRepository;
    private readonly McpToolRunner _runner;

    public NportTools(
        NportFilingRepository nportRepository,
        CommonStockRepository commonStockRepository,
        FundSeriesRepository fundSeriesRepository,
        ErrorManager errorManager,
        ILogger<NportTools> logger
    )
    {
        _nportRepository = nportRepository;
        _commonStockRepository = commonStockRepository;
        _fundSeriesRepository = fundSeriesRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetFundHoldings", Title = "Fund Portfolio Holdings", ReadOnly = true)]
    [Description(
        "Get the largest stored portfolio holdings of a registered investment company (mutual fund or ETF) from its most recent SEC Form NPORT-P monthly report. Accepts a fund ticker, profile id, SEC series id, or verified share-class alias from SearchFunds. Returns the fund's series, reporting period, net assets, full reported holding count when available, stored holding count, and largest stored holdings. For multi-series trusts only positions whose CUSIPs match tracked stocks are stored, so the stored rows can be a small subset of the reported portfolio; net assets and the reported count still describe the full filing. Use SearchFunds to discover funds, GetFundProfile for the same view with registrant and total assets, and GetFundsHoldingStock for the inverse question. Form NPORT-P covers registered management investment companies and ETFs organized as unit investment trusts; money market funds and small business investment companies do not file it."
    )]
    public Task<string> GetFundHoldings(
        [Description(
            "Fund or ETF ticker, profile id, SEC series id, or verified share-class alias from SearchFunds (e.g., SPY, 'vanguard-500-index-fund-s000002839', S000002839, or VOO)"
        )]
            string ticker,
        [Description("Maximum number of holdings to return, largest first (default: 20, max: 500)")]
            int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (filing, fundName, error) = await ResolveLatestFiling(ticker);
                if (error != null)
                    return error;

                var holdings = filing
                    .Holdings.OrderByDescending(h => h.ValueUsd)
                    .Take(McpLimit.Clamp(maxResults))
                    .ToList();

                var storedCountLabel =
                    filing.CommonStockId == null
                        ? $"{filing.Holdings.Count} stored tracked-stock holdings"
                        : $"{filing.Holdings.Count} stored holdings";

                var result = MarkdownTable.Start(
                    $"Portfolio holdings for {MarkdownText(fundName)} ({MarkdownText(ticker)}) — "
                        + $"reported {filing.ReportPeriodDate:yyyy-MM-dd}, net assets ${FormatAmount(filing.NetAssets)}, "
                        + $"{FormatCount(filing.ReportedHoldingCount)} holdings reported, {storedCountLabel}, "
                        + $"showing the largest {holdings.Count} stored rows:",
                    "| Holding | CUSIP | Balance | Units | Value (USD) | % Net Assets | Category | Country |",
                    "|---------|-------|---------|-------|-------------|--------------|----------|---------|"
                );

                result.AppendRows(
                    holdings,
                    h =>
                        $"| {MarkdownText(h.Name) ?? "-"} | {MarkdownText(h.Cusip) ?? "-"} | {FundCodes.Balance(h.Balance)} | {MarkdownText(FundCodes.Unit(h.Units))} | ${FormatAmount(h.ValueUsd)} | {FormatPercent(h.PercentValue)} | {MarkdownText(FundCodes.AssetCategory(h.AssetCategory))} | {MarkdownText(h.InvestmentCountry) ?? "-"} |"
                );

                TruncationNotes.Append(result, holdings.Count, filing.Holdings.Count);

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

    /// <summary>
    /// Resolves the identifier to the fund's most recent NPORT-P report through the same exact
    /// fund-series tiers <c>SearchFunds</c> and <c>GetFundProfile</c> use. A canonical profile id,
    /// SEC series id, stored series ticker, or verified share-class alias is authoritative and
    /// keeps the filing query constrained to that series. Only identifiers absent from the fund
    /// directory fall back to a tracked stock ticker. The latest report is the one with the
    /// greatest report period (filing date as tiebreaker), so a late-filed amendment of an older
    /// period never shadows the newest period.
    /// </summary>
    private async Task<(NportFiling Filing, string FundName, string Error)> ResolveLatestFiling(
        string identifier
    )
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return (null, null, "Provide a fund ticker or a profile id from SearchFunds.");

        var series = await _fundSeriesRepository
            .ResolveIdentifier(identifier)
            .OrderByDescending(f => f.NetAssets)
            .FirstOrDefaultAsync();

        if (series != null)
        {
            var latest = await _nportRepository
                .GetSeriesReportsByPeriod(
                    series.CommonStockId,
                    series.RegistrantCik,
                    series.SeriesId,
                    DateOnly.MinValue
                )
                .Include(f => f.Holdings)
                .OrderByDescending(f => f.ReportPeriodDate)
                .FirstOrDefaultAsync();

            return latest == null
                ? (
                    null,
                    null,
                    $"No stored Form NPORT-P report is on record for {MarkdownText(series.SeriesName ?? series.RegistrantName)}. This is a dataset coverage result, not evidence that no SEC filing exists; the report may be outside the filing scope or absent from this ingestion, fetch, or replay state."
                )
                : (latest, series.SeriesName ?? series.RegistrantName, null);
        }

        var (stock, _) = await _commonStockRepository.ResolveByTicker(identifier);
        if (stock != null)
        {
            var filing = await _nportRepository
                .GetByStock(stock)
                .Include(f => f.Holdings)
                .OrderByDescending(f => f.ReportPeriodDate)
                .ThenByDescending(f => f.FilingDate)
                .FirstOrDefaultAsync();

            return filing == null
                ? (
                    null,
                    null,
                    $"No stored Form NPORT-P portfolio report was found for {MarkdownText(identifier)}. This is a dataset coverage result, not evidence that no SEC filing exists; the report may be outside the filing scope or absent from this ingestion, fetch, or replay state."
                )
                : (filing, filing.SeriesName ?? stock.Name, null);
        }

        return (
            null,
            null,
            $"No fund or ETF found for '{MarkdownText(identifier)}' in the tracked Form NPORT-P directory. Use SearchFunds to find a profile id. Form NPORT-P covers registered management investment companies and ETFs organized as unit investment trusts; money market funds and small business investment companies do not file it. Fixed-income-only series and vehicles outside that filing regime may be absent. This is a coverage result, not evidence that the fund does not exist."
        );
    }

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
            string registrantOrSeries = null
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

                var positions = await currentPositions
                    .OrderByDescending(p => p.ValueUsd)
                    .Take(McpLimit.Clamp(maxResults))
                    .ToListAsync();

                var filterLabel = string.IsNullOrWhiteSpace(registrantOrSeries)
                    ? ""
                    : $" matching '{MarkdownText(registrantOrSeries)}'";
                var result = MarkdownTable.Start(
                    $"Funds holding {MarkdownText(stock.Name)} ({safeTicker}) on each series' most recent Form NPORT-P — "
                        + $"{totalCount} current fund positions{filterLabel}, showing the largest {positions.Count}. "
                        + "Report dates differ per series (each fund's own fiscal quarter):",
                    "| Registrant | Series | Report Date | Balance | Units | Value (USD) | % Net Assets | Long/Short |",
                    "|------------|--------|-------------|---------|-------|-------------|--------------|------------|"
                );

                result.AppendRows(
                    positions,
                    p =>
                        $"| {MarkdownText(p.RegistrantName) ?? "-"} | {MarkdownText(p.SeriesName) ?? "-"} | {p.ReportPeriodDate:yyyy-MM-dd} | {FormatAmount(p.Balance)} | {MarkdownText(FundCodes.Unit(p.Units))} | ${FormatAmount(p.ValueUsd)} | {FormatPercent(p.PercentValue)} | {MarkdownText(p.PayoffProfile) ?? "-"} |"
                );

                return result.ToString();
            },
            "GetFundsHoldingStock",
            $"ticker: {ticker}"
        );
    }

    private static string FormatAmount(decimal value) => McpFormat.Invariant(value, "N2");

    private static string FormatPercent(decimal value) => McpFormat.Invariant(value, "N2") + "%";

    private static string FormatCount(int? value) =>
        value.HasValue ? McpFormat.WholeNumber(value.Value) : "unavailable";

    private static string MarkdownText(string value) =>
        value == null ? null : MarkdownTable.EscapeCell(value).Trim();
}
