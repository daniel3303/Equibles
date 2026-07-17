using System.ComponentModel;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Sec.Mcp.Tools;

/// <summary>
/// Browse the registered-fund directory built from SEC Form NPORT-P reports. Unlike
/// <c>GetFundHoldings</c> (which is keyed by a fund's own ticker), these tools reach every fund
/// series we ingest — including the big multi-series fund-family trusts (iShares, Vanguard,
/// Fidelity) that have no ticker of their own — via the materialised fund directory.
/// </summary>
[McpServerToolType]
public class FundDirectoryTools
{
    private readonly FundSeriesRepository _fundSeriesRepository;
    private readonly NportFilingRepository _nportRepository;
    private readonly McpToolRunner _runner;

    public FundDirectoryTools(
        FundSeriesRepository fundSeriesRepository,
        NportFilingRepository nportRepository,
        ErrorManager errorManager,
        ILogger<FundDirectoryTools> logger
    )
    {
        _fundSeriesRepository = fundSeriesRepository;
        _nportRepository = nportRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "SearchFunds")]
    [Description(
        "Search the directory of registered investment companies (mutual funds and ETFs) that file SEC Form NPORT-P, by fund name, ticker or registrant. Returns each matching fund series with its profile id (use it with GetFundProfile), ticker (when the fund is itself listed), registration type (from N-CEN, when on record), net assets, number of reported holdings and latest report date, largest funds first. Covers the large multi-series trusts (iShares, Vanguard, Fidelity) that have no ticker of their own. Only a fund's own series-level ticker matches (e.g. IWM, SPY); share-class tickers of multi-class mutual funds (e.g. VOO, VFIAX) are not indexed — search those by fund name instead (e.g. 'Vanguard 500')."
    )]
    public Task<string> SearchFunds(
        [Description(
            "Fund name, ticker or registrant to search for (e.g., 'Russell 2000', 'iShares', 'IWM'). Share-class tickers of multi-class mutual funds (e.g. VOO) do not match — use the fund's name."
        )]
            string query,
        [Description(
            "Maximum number of funds to return, largest by net assets first (default: 20, max: 500)"
        )]
            int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(query))
                    return "Provide a fund name, ticker or registrant to search for.";

                var term = query.Trim().ToLower();
                var allMatches = _fundSeriesRepository
                    .GetAll()
                    .Where(f =>
                        (f.SeriesName != null && f.SeriesName.ToLower().Contains(term))
                        || (f.RegistrantName != null && f.RegistrantName.ToLower().Contains(term))
                        || (f.Ticker != null && f.Ticker.ToLower().Contains(term))
                    );

                var totalCount = await allMatches.CountAsync();
                if (totalCount == 0)
                    return $"No registered funds match '{query}'. Share-class tickers of multi-class mutual funds (e.g. VOO, VFIAX) are not searchable — try the fund's name instead.";

                var matches = await allMatches
                    .OrderByDescending(f => f.NetAssets)
                    .Take(McpLimit.Clamp(maxResults))
                    .ToListAsync();

                var result = MarkdownTable.Start(
                    $"Registered funds matching '{query}', largest by net assets first (showing {matches.Count} of {totalCount}):",
                    "| Fund | Profile id | Ticker | Type | Net Assets (USD) | Holdings | Latest Report |",
                    "|------|-----------|--------|------|------------------|----------|---------------|"
                );

                result.AppendRows(
                    matches,
                    f =>
                        $"| {f.SeriesName ?? f.RegistrantName ?? "-"} | {f.Slug} | {f.Ticker ?? "-"} | {FundCodes.RegistrationType(f.FundType)} | ${FormatAmount(f.NetAssets)} | {f.PositionCount} | {f.LatestReportPeriodDate:yyyy-MM-dd} |"
                );

                TruncationNotes.Append(result, matches.Count, totalCount);

                return result.ToString();
            },
            "SearchFunds",
            $"query: {query}"
        );
    }

    [McpServerTool(Name = "GetFundProfile")]
    [Description(
        "Get a registered fund's profile and largest holdings from its most recent SEC Form NPORT-P report. Accepts a fund profile id from SearchFunds or a fund's own ticker. Returns the fund's registrant and series, reporting period, net and total assets, then its largest holdings — issuer name, CUSIP, position size, U.S.-dollar value, share of net assets and asset category. Prefer this after SearchFunds: the profile id reaches the many fund series that have no ticker of their own; GetFundHoldings is the equivalent view, and GetFundsHoldingStock answers the inverse question (which funds own a stock). For the large multi-series trusts only positions in tracked stocks are stored, so the holdings shown are the fund's tracked-stock positions; the net-asset totals are the fund's real totals."
    )]
    public Task<string> GetFundProfile(
        [Description(
            "Fund profile id from SearchFunds (e.g., 'ishares-russell-2000-etf-s000004344') or a fund's own ticker (e.g., 'IWM'). Share-class tickers of multi-class mutual funds (e.g. VOO, VFIAX) do not resolve — find the fund by name via SearchFunds."
        )]
            string fund,
        [Description("Maximum number of holdings to return, largest first (default: 20, max: 500)")]
            int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(fund))
                    return "Provide a fund profile id or ticker.";

                var key = fund.Trim();
                var lowerKey = key.ToLower();
                var series = await _fundSeriesRepository
                    .GetAll()
                    .Where(f =>
                        f.Slug == key || (f.Ticker != null && f.Ticker.ToLower() == lowerKey)
                    )
                    .OrderByDescending(f => f.NetAssets)
                    .FirstOrDefaultAsync();

                if (series == null)
                    return $"No registered fund found for '{fund}'. Use SearchFunds to find a fund's profile id — share-class tickers of multi-class mutual funds (e.g. VOO, VFIAX) do not resolve, so search by fund name.";

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

                if (latest == null)
                    return $"No Form NPORT-P report is on record for {series.SeriesName ?? series.RegistrantName}.";

                var holdings = latest
                    .Holdings.OrderByDescending(h => h.ValueUsd)
                    .Take(McpLimit.Clamp(maxResults))
                    .ToList();

                var header =
                    $"{series.SeriesName ?? series.RegistrantName}"
                    + (series.Ticker != null ? $" ({series.Ticker})" : "")
                    + $" — registrant {series.RegistrantName ?? "-"}, "
                    + $"reported {latest.ReportPeriodDate:yyyy-MM-dd}, "
                    + $"net assets ${FormatAmount(latest.NetAssets)}, total assets ${FormatAmount(latest.TotalAssets)}, "
                    + $"{latest.Holdings.Count} holdings on record, showing the largest {holdings.Count}:";

                var result = MarkdownTable.Start(
                    header,
                    "| Holding | CUSIP | Balance | Units | Value (USD) | % Net Assets | Category | Country |",
                    "|---------|-------|---------|-------|-------------|--------------|----------|---------|"
                );

                result.AppendRows(
                    holdings,
                    h =>
                        $"| {h.Name ?? "-"} | {h.Cusip ?? "-"} | {FundCodes.Balance(h.Balance)} | {FundCodes.Unit(h.Units)} | ${FormatAmount(h.ValueUsd)} | {FormatPercent(h.PercentValue)} | {FundCodes.AssetCategory(h.AssetCategory)} | {h.InvestmentCountry ?? "-"} |"
                );

                TruncationNotes.Append(result, holdings.Count, latest.Holdings.Count);

                return result.ToString();
            },
            "GetFundProfile",
            $"fund: {fund}"
        );
    }

    private static string FormatAmount(decimal value) => McpFormat.Invariant(value, "N2");

    private static string FormatPercent(decimal value) => McpFormat.Invariant(value, "N2") + "%";
}
