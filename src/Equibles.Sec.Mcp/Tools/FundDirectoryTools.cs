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

    [McpServerTool(Name = "SearchFunds", Title = "Search Funds and ETFs", ReadOnly = true)]
    [Description(
        "Search the tracked SEC Form NPORT-P fund directory by fund name, ticker, or registrant. Search first requires every punctuation-independent query word anywhere across those fields, then broadens to any word only when no strict row matches. Verified share-class aliases such as VOO and VFIAX resolve to their SEC fund series even when N-PORT carries no class ticker. Returns profile id, ticker when present, registration type, net assets, stored holding count, and latest report date, largest funds first."
    )]
    public Task<string> SearchFunds(
        [Description(
            "Fund name, ticker, registrant, or verified share-class alias (e.g., 'Russell 2000', 'iShares', 'IWM', 'VOO')."
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

                var allMatches = _fundSeriesRepository.Search(query);

                var totalCount = await allMatches.CountAsync();
                if (totalCount == 0)
                    return $"No match for '{query}' in the tracked Form NPORT-P fund directory. This does not assert that the fund does not exist or file with the SEC; try fewer words or list another identifier.";

                var matches = await allMatches
                    .OrderByDescending(f => f.NetAssets)
                    .Take(McpLimit.Clamp(maxResults))
                    .ToListAsync();

                var result = MarkdownTable.Start(
                    $"Registered funds matching '{query}', largest by net assets first (showing {matches.Count} of {totalCount}):",
                    // Sweep-discovered series only have their tracked-stock positions stored,
                    // so a bond fund's Holdings can read 0 beside real net assets — say so, or
                    // the count reads as "this fund holds nothing".
                    "_Holdings = stored holding rows on the fund's latest report. For the large multi-series trusts only positions in stocks this platform tracks are stored, so the count can be a small subset of (or zero within) the real portfolio; Net Assets is always the fund's own reported total._",
                    "| Fund | Profile id | Ticker | Type | Net Assets (USD) | Holdings | Latest Report |",
                    "|------|-----------|--------|------|------------------|----------|---------------|"
                );

                result.AppendRows(
                    matches,
                    f =>
                        $"| {MarkdownTable.EscapeCell(f.SeriesName ?? f.RegistrantName, "-")} | {MarkdownTable.EscapeCell(f.Slug, "-")} | {MarkdownTable.EscapeCell(f.Ticker, "-")} | {MarkdownTable.EscapeCell(FundCodes.RegistrationType(f.FundType), "-")} | ${FormatAmount(f.NetAssets)} | {f.PositionCount} | {f.LatestReportPeriodDate:yyyy-MM-dd} |"
                );

                TruncationNotes.Append(result, matches.Count, totalCount);

                return result.ToString();
            },
            "SearchFunds",
            $"query: {query}"
        );
    }

    [McpServerTool(
        Name = "GetFundProfile",
        Title = "Fund Profile and Top Holdings",
        ReadOnly = true
    )]
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
