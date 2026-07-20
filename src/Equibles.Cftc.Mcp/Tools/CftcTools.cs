using System.ComponentModel;
using Equibles.Cftc.Data.Models;
using Equibles.Cftc.Repositories;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Cftc.Mcp.Tools;

[McpServerToolType]
public class CftcTools
{
    private const string AcceptedCategories =
        "Agriculture, Energy, Metals, EquityIndices, InterestRates, Currencies";

    private readonly CftcContractRepository _contractRepository;
    private readonly CftcPositionReportRepository _reportRepository;
    private readonly McpToolRunner _runner;

    public CftcTools(
        CftcContractRepository contractRepository,
        CftcPositionReportRepository reportRepository,
        ErrorManager errorManager,
        ILogger<CftcTools> logger
    )
    {
        _contractRepository = contractRepository;
        _reportRepository = reportRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(
        Name = "GetCftcPositioning",
        Title = "CFTC Futures Positioning (COT)",
        ReadOnly = true
    )]
    [Description(
        "Get Commitments of Traders (COT) positioning data for a specific futures contract. "
            + "Shows commercial and non-commercial positions over time. Values are contract "
            + "counts from the legacy futures-only COT report (positions as of each Tuesday, "
            + "published Friday). Use SearchCftcMarkets to find available market codes."
    )]
    public Task<string> GetCftcPositioning(
        [Description(
            "CFTC contract market code (e.g., 067651 for Crude Oil, 088691 for Gold, 13874A for E-mini S&P 500)"
        )]
            string marketCode,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description(
            "Maximum number of reports to return (default: 52, max: 500). When the range holds more reports the newest are kept; rows are always listed oldest to newest."
        )]
            int maxResults = 52
    )
    {
        return _runner.Execute(
            async () =>
            {
                var contract = await _contractRepository
                    .GetByMarketCode(marketCode.Trim())
                    .FirstOrDefaultAsync();

                if (contract == null)
                    return $"Contract '{marketCode}' not found. Use SearchCftcMarkets to find available contracts.";

                var rangeError = ParseRangeStrict(
                    startDate,
                    endDate,
                    McpToolExecutor.UtcYearsAgo(1),
                    out var start,
                    out var end
                );
                if (rangeError != null)
                    return rangeError;

                maxResults = McpLimit.Clamp(maxResults);

                var rangeQuery = _reportRepository.GetByContract(contract, start, end);
                var total = await rangeQuery.CountAsync();

                var reports = await rangeQuery
                    .OrderByDescending(r => r.ReportDate)
                    .Take(maxResults)
                    .ToListAsync();

                var table = MarkdownTable.Render(
                    reports.OrderBy(r => r.ReportDate).ToList(),
                    $"No COT reports found for {contract.MarketName} ({contract.MarketCode}) in the specified date range.",
                    $"{contract.MarketName} ({contract.MarketCode}) — {contract.Category.NameForHumans()}",
                    "| Date | Open Interest | Comm Long | Comm Short | Non-Comm Long | Non-Comm Short | Non-Comm Spread |",
                    "|------|--------------|-----------|------------|---------------|----------------|-----------------|",
                    r =>
                        $"| {r.ReportDate:yyyy-MM-dd} | {McpFormat.WholeNumber(r.OpenInterest)} | {McpFormat.WholeNumber(r.CommLong)} | {McpFormat.WholeNumber(r.CommShort)} | {McpFormat.WholeNumber(r.NonCommLong)} | {McpFormat.WholeNumber(r.NonCommShort)} | {McpFormat.WholeNumber(r.NonCommSpreads)} |"
                );

                if (reports.Count < total)
                    table +=
                        Environment.NewLine
                        + $"_Showing the newest {reports.Count} of {total} reports in the range - raise maxResults or narrow the date range to see older reports._";

                return table;
            },
            "GetCftcPositioning",
            $"marketCode: {marketCode}"
        );
    }

    [McpServerTool(
        Name = "GetLatestCftcData",
        Title = "Latest CFTC Positioning Snapshot",
        ReadOnly = true
    )]
    [Description(
        "Get the latest COT positioning snapshot across all tracked futures contracts, grouped "
            + "by category (Agriculture, Energy, Metals, Equity Indices, Interest Rates, "
            + "Currencies). Shows commercial and non-commercial net positions in contract "
            + "counts from the legacy futures-only COT report (positions as of each Tuesday, "
            + "published Friday). Each row carries the market code accepted by GetCftcPositioning."
    )]
    public Task<string> GetLatestCftcData(
        [Description(
            "Category filter: Agriculture, Energy, Metals, EquityIndices, InterestRates, Currencies (defaults to all)"
        )]
            string category = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                IQueryable<CftcContract> contractQuery;
                CftcContractCategory? parsedCategory = null;

                if (!string.IsNullOrWhiteSpace(category))
                {
                    parsedCategory = ParseCategory(category);
                    if (parsedCategory == null)
                        return McpOutput.InvalidArgument("category", category, AcceptedCategories);
                    contractQuery = _contractRepository.GetByCategory(parsedCategory.Value);
                }
                else
                {
                    contractQuery = _contractRepository.GetAll();
                }

                var contracts = await contractQuery
                    .OrderBy(c => c.Category)
                    .ThenBy(c => c.MarketName)
                    .ToListAsync();

                if (contracts.Count == 0)
                    return parsedCategory != null
                        ? $"No tracked contracts in the {parsedCategory.Value.NameForHumans()} category."
                        : "No CFTC contracts found in the database.";

                var latestReports = await _reportRepository
                    .GetLatestPerContract()
                    .ToDictionaryAsync(r => r.CftcContractId);

                var result = MarkdownTable.Start(
                    "Latest COT Positioning:",
                    "| Market | Code | Date | Open Interest | Comm Net | Non-Comm Net |",
                    "|--------|------|------|--------------|----------|--------------|"
                );

                CftcContractCategory? currentCategory = null;

                foreach (var contract in contracts)
                {
                    if (currentCategory != contract.Category)
                    {
                        currentCategory = contract.Category;
                        result.AppendLine($"| **{contract.Category.NameForHumans()}** | | | | | |");
                    }

                    latestReports.TryGetValue(contract.Id, out var report);

                    var dateStr = McpFormat.OrDash(report?.ReportDate, "yyyy-MM-dd");
                    var oiStr = McpFormat.OrDash(report?.OpenInterest, "N0");
                    var commNet =
                        report != null
                            ? McpFormat.WholeNumber(report.CommLong - report.CommShort)
                            : "—";
                    var nonCommNet =
                        report != null
                            ? McpFormat.WholeNumber(report.NonCommLong - report.NonCommShort)
                            : "—";

                    result.AppendLine(
                        $"| {contract.MarketName} | {contract.MarketCode} | {dateStr} | {oiStr} | {commNet} | {nonCommNet} |"
                    );
                }

                result.AppendLine();
                result.AppendLine(
                    "_Values are contract counts. Use GetCftcPositioning(marketCode) for a contract's positioning history._"
                );

                return result.ToString();
            },
            "GetLatestCftcData",
            $"category: {category}"
        );
    }

    [McpServerTool(
        Name = "SearchCftcMarkets",
        Title = "Search CFTC Futures Contracts",
        ReadOnly = true
    )]
    [Description(
        "Search the tracked CFTC futures contracts by name or market code, or omit the query "
            + "to list every tracked contract. Coverage is a curated set of ~35 major contracts "
            + "across Agriculture, Energy, Metals, Equity Indices, Interest Rates, and "
            + "Currencies - markets outside this set have no COT data here. Returns matching "
            + "contracts with their codes and categories; use this to discover market codes "
            + "before calling GetCftcPositioning."
    )]
    public Task<string> SearchCftcMarkets(
        [Description(
            "Search query — market code or name keyword (e.g., 'gold', 'crude', 'S&P', '088691'). Omit to list all tracked contracts."
        )]
            string query = null,
        [Description("Maximum number of results to return (default: 50, max: 500)")]
            int maxResults = 50
    )
    {
        return _runner.Execute(
            async () =>
            {
                maxResults = McpLimit.Clamp(maxResults);

                var matchQuery = string.IsNullOrWhiteSpace(query)
                    ? _contractRepository.GetAll()
                    : _contractRepository.Search(query.Trim());

                var total = await matchQuery.CountAsync();

                var contracts = await matchQuery
                    .OrderBy(c => c.Category)
                    .ThenBy(c => c.MarketName)
                    .Take(maxResults)
                    .ToListAsync();

                var table = MarkdownTable.Render(
                    contracts,
                    string.IsNullOrWhiteSpace(query)
                        ? "No CFTC contracts found in the database."
                        : $"No tracked contracts match '{query}'. Coverage is a curated set of ~35 major contracts - omit the query to list them all.",
                    string.IsNullOrWhiteSpace(query)
                        ? "Tracked CFTC contracts:"
                        : $"CFTC contracts matching '{query}':",
                    "| Market Code | Name | Category |",
                    "|-------------|------|----------|",
                    c => $"| {c.MarketCode} | {c.MarketName} | {c.Category.NameForHumans()} |"
                );

                var note = McpOutput.TruncationNote(contracts.Count, total);
                if (note.Length > 0)
                    table += Environment.NewLine + note;

                return table;
            },
            "SearchCftcMarkets",
            $"query: {query}"
        );
    }

    // Maps the caller's category to the enum, accepting both the enum spelling
    // (InterestRates) and the display spelling the tool's own output prints
    // ("Interest Rates"). Matches by name so numeric strings never slip through
    // Enum.TryParse into an unfiltered or undefined query.
    private static CftcContractCategory? ParseCategory(string category)
    {
        var normalized = category.Replace(" ", "").Trim();
        foreach (var candidate in Enum.GetValues<CftcContractCategory>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        return null;
    }

    // Strict argument parsing shared by the date-ranged CFTC tools: a non-empty date must
    // be exactly yyyy-MM-dd (no silent fallback to the default window), and an inverted
    // range is a caller error rather than an empty-looking result.
    private static string ParseRangeStrict(
        string startDate,
        string endDate,
        DateOnly defaultStart,
        out DateOnly start,
        out DateOnly end
    )
    {
        start = defaultStart;
        end = DateOnly.FromDateTime(DateTime.UtcNow);

        if (!string.IsNullOrWhiteSpace(startDate))
        {
            if (!McpOutput.TryParseDate(startDate, out var parsedStart))
                return McpOutput.InvalidArgument("startDate", startDate, "yyyy-MM-dd");
            start = DateOnly.FromDateTime(parsedStart);
        }

        if (!string.IsNullOrWhiteSpace(endDate))
        {
            if (!McpOutput.TryParseDate(endDate, out var parsedEnd))
                return McpOutput.InvalidArgument("endDate", endDate, "yyyy-MM-dd");
            end = DateOnly.FromDateTime(parsedEnd);
        }

        if (start > end)
            return $"startDate ({start:yyyy-MM-dd}) is after endDate ({end:yyyy-MM-dd}) - swap the dates.";

        return null;
    }
}
