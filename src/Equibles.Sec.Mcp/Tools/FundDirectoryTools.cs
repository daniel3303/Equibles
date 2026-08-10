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
        "Search the tracked SEC Form NPORT-P fund directory by fund name, ticker, or registrant. Search first requires every punctuation-independent query word anywhere across those fields, then broadens to any word only when no strict row matches. Verified share-class aliases such as VOO and VFIAX resolve to their SEC fund series even when N-PORT carries no class ticker. Returns profile id, ticker when present, registration type, net assets, stored holding count, the fund's full reported holding count when available, and latest report date, largest funds first. For multi-series trusts the stored count includes only positions whose CUSIPs match tracked stocks. Form NPORT-P covers registered management investment companies and ETFs organized as unit investment trusts; money market funds and small business investment companies do not file it. Fixed-income-only series can be absent because trust reports enter this tracked directory after a tracked-stock match."
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

                var safeQuery = MarkdownText(query);
                var allMatches = _fundSeriesRepository.Search(query);

                var totalCount = await allMatches.CountAsync();
                if (totalCount == 0)
                    return $"No match for '{safeQuery}' in the tracked Form NPORT-P fund directory. Form NPORT-P covers registered management investment companies and ETFs organized as unit investment trusts; money market funds and small business investment companies do not file it. Operating companies, BDCs, unregistered private funds, and other filers outside that form are also out of scope; registered private-credit funds can be in scope. Fixed-income-only series can be absent because trust reports enter this directory after a tracked-stock match. This is a coverage result, not evidence that the fund does not exist; try fewer words or another identifier.";

                var matches = await allMatches
                    .OrderByDescending(f => f.NetAssets)
                    .Take(McpLimit.Clamp(maxResults))
                    .ToListAsync();

                var result = MarkdownTable.Start(
                    $"Registered funds matching '{safeQuery}', largest by net assets first (showing {matches.Count} of {totalCount}):",
                    // Sweep-discovered series only have their tracked-stock positions stored,
                    // so a bond fund's Holdings can read 0 beside real net assets — say so, or
                    // the count reads as "this fund holds nothing".
                    "_Stored = holding rows retained by this platform; for multi-series trusts that is only positions whose CUSIPs match tracked stocks. Reported = the full investment-row count in the fund's filing before that filter (`—` means parser replay is pending or EDGAR could not be re-fetched). Net Assets is always the fund's reported total._",
                    "| Fund | Profile id | Ticker | Type | Net Assets (USD) | Stored | Reported | Latest Report |",
                    "|------|-----------|--------|------|------------------|--------|----------|---------------|"
                );

                result.AppendRows(
                    matches,
                    f =>
                        $"| {MarkdownTable.EscapeCell(f.SeriesName ?? f.RegistrantName, "-")} | {MarkdownTable.EscapeCell(f.Slug, "-")} | {MarkdownTable.EscapeCell(f.Ticker, "-")} | {MarkdownTable.EscapeCell(FundCodes.RegistrationType(f.FundType), "-")} | ${FormatAmount(f.NetAssets)} | {f.PositionCount} | {FormatCount(f.ReportedHoldingCount)} | {f.LatestReportPeriodDate:yyyy-MM-dd} |"
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
        "Get a registered fund's profile and largest stored holdings from its most recent SEC Form NPORT-P report. Accepts a profile id, stored series ticker, SEC series id, or verified share-class alias from SearchFunds. Returns the fund's registrant and series, reporting period, net and total assets, full reported holding count when available, stored holding count, then its largest stored holdings — issuer name, CUSIP, position size, U.S.-dollar value, share of net assets and asset category. Prefer this after SearchFunds; GetFundHoldings is the equivalent view, and GetFundsHoldingStock answers the inverse question. For large multi-series trusts only positions in tracked stocks are stored; the reported count and asset totals still describe the fund's full filing."
    )]
    public Task<string> GetFundProfile(
        [Description(
            "Fund profile id, SEC series id, stored series ticker, or verified share-class alias from SearchFunds (e.g., 'ishares-russell-2000-etf-s000004344', 'S000004344', 'IWM', or 'VOO')."
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

                var safeFund = MarkdownText(fund);
                var series = await _fundSeriesRepository
                    .ResolveIdentifier(fund)
                    .OrderByDescending(f => f.NetAssets)
                    .FirstOrDefaultAsync();

                if (series == null)
                    return $"No registered fund found for '{safeFund}' in the tracked Form NPORT-P directory. Use SearchFunds to find a profile id. Form NPORT-P covers registered management investment companies and ETFs organized as unit investment trusts; money market funds and small business investment companies do not file it. Fixed-income-only series and vehicles outside that filing regime may be absent. This is a coverage result, not evidence that the fund does not exist.";

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
                    return $"No stored Form NPORT-P report is on record for {MarkdownText(series.SeriesName ?? series.RegistrantName)}. This is a dataset coverage result, not evidence that no SEC filing exists; the report may be outside the filing scope or absent from this ingestion, fetch, or replay state.";

                var holdings = latest
                    .Holdings.OrderByDescending(h => h.ValueUsd)
                    .Take(McpLimit.Clamp(maxResults))
                    .ToList();

                var header =
                    $"{MarkdownText(series.SeriesName ?? series.RegistrantName)}"
                    + (series.Ticker != null ? $" ({MarkdownText(series.Ticker)})" : "")
                    + $" — registrant {MarkdownText(series.RegistrantName) ?? "-"}, "
                    + $"reported {latest.ReportPeriodDate:yyyy-MM-dd}, "
                    + $"net assets ${FormatAmount(latest.NetAssets)}, total assets ${FormatAmount(latest.TotalAssets)}, "
                    + $"{FormatCount(latest.ReportedHoldingCount)} holdings reported, {latest.Holdings.Count} stored"
                    + (latest.CommonStockId == null ? " tracked-stock holdings" : " holdings")
                    + $", showing the largest {holdings.Count} stored rows:";

                var result = MarkdownTable.Start(
                    header,
                    "| Holding | CUSIP | Balance | Units | Value (USD) | % Net Assets | Category | Country |",
                    "|---------|-------|---------|-------|-------------|--------------|----------|---------|"
                );

                result.AppendRows(
                    holdings,
                    h =>
                        $"| {MarkdownText(h.Name) ?? "-"} | {MarkdownText(h.Cusip) ?? "-"} | {FundCodes.Balance(h.Balance)} | {MarkdownText(FundCodes.Unit(h.Units))} | ${FormatAmount(h.ValueUsd)} | {FormatPercent(h.PercentValue)} | {MarkdownText(FundCodes.AssetCategory(h.AssetCategory))} | {MarkdownText(h.InvestmentCountry) ?? "-"} |"
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

    private static string FormatCount(int? value) => McpFormat.OrDash(value, "N0");

    private static string MarkdownText(string value) =>
        value == null ? null : MarkdownTable.EscapeCell(value).Trim();
}
