using System.ComponentModel;
using System.Globalization;
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

    [McpServerTool(Name = "GetCftcPositioning")]
    [Description(
        "Get Commitments of Traders (COT) positioning data for a specific futures contract. Shows commercial and non-commercial positions over time. Use SearchCftcMarkets to find available market codes."
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
        [Description("Maximum number of reports to return (default: 52, newest first)")]
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

                var (start, end) = McpToolExecutor.ParseDateRange(
                    startDate,
                    endDate,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1))
                );

                var reports = await _reportRepository
                    .GetByContract(contract, start, end)
                    .OrderByDescending(r => r.ReportDate)
                    .Take(Math.Max(0, maxResults))
                    .ToListAsync();

                if (reports.Count == 0)
                    return $"No COT reports found for {contract.MarketName} ({contract.MarketCode}) in the specified date range.";

                var result = MarkdownTable.Start(
                    $"{contract.MarketName} ({contract.MarketCode}) — {contract.Category.NameForHumans()}",
                    "| Date | Open Interest | Comm Long | Comm Short | Non-Comm Long | Non-Comm Short | Non-Comm Spread |",
                    "|------|--------------|-----------|------------|---------------|----------------|-----------------|"
                );

                foreach (var r in reports.OrderBy(r => r.ReportDate))
                {
                    result.AppendLine(
                        $"| {r.ReportDate:yyyy-MM-dd} | {r.OpenInterest.ToString("N0", CultureInfo.InvariantCulture)} | {r.CommLong.ToString("N0", CultureInfo.InvariantCulture)} | {r.CommShort.ToString("N0", CultureInfo.InvariantCulture)} | {r.NonCommLong.ToString("N0", CultureInfo.InvariantCulture)} | {r.NonCommShort.ToString("N0", CultureInfo.InvariantCulture)} | {r.NonCommSpreads.ToString("N0", CultureInfo.InvariantCulture)} |"
                    );
                }

                return result.ToString();
            },
            "GetCftcPositioning",
            $"marketCode: {marketCode}"
        );
    }

    [McpServerTool(Name = "GetLatestCftcData")]
    [Description(
        "Get the latest COT positioning snapshot across all tracked futures contracts, grouped by category (Agriculture, Energy, Metals, Equity Indices, Interest Rates, Currencies). Shows commercial and non-commercial net positions."
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

                if (
                    !string.IsNullOrEmpty(category)
                    && Enum.TryParse<CftcContractCategory>(category, true, out var parsedCategory)
                )
                {
                    contractQuery = _contractRepository.GetByCategory(parsedCategory);
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
                    return "No CFTC contracts found in the database.";

                var latestReports = await _reportRepository
                    .GetLatestPerContract()
                    .ToDictionaryAsync(r => r.CftcContractId);

                var result = MarkdownTable.Start(
                    "Latest COT Positioning:",
                    "| Market | Date | Open Interest | Comm Net | Non-Comm Net |",
                    "|--------|------|--------------|----------|--------------|"
                );

                CftcContractCategory? currentCategory = null;

                foreach (var contract in contracts)
                {
                    if (currentCategory != contract.Category)
                    {
                        currentCategory = contract.Category;
                        result.AppendLine($"| **{contract.Category.NameForHumans()}** | | | | |");
                    }

                    latestReports.TryGetValue(contract.Id, out var report);

                    var dateStr = McpFormat.OrDash(report?.ReportDate, "yyyy-MM-dd");
                    var oiStr = McpFormat.OrDash(report?.OpenInterest, "N0");
                    var commNet =
                        report != null ? (report.CommLong - report.CommShort).ToString("N0") : "—";
                    var nonCommNet =
                        report != null
                            ? (report.NonCommLong - report.NonCommShort).ToString("N0")
                            : "—";

                    result.AppendLine(
                        $"| {contract.MarketName} | {dateStr} | {oiStr} | {commNet} | {nonCommNet} |"
                    );
                }

                return result.ToString();
            },
            "GetLatestCftcData",
            $"category: {category}"
        );
    }

    [McpServerTool(Name = "SearchCftcMarkets")]
    [Description(
        "Search for available CFTC futures contracts by name or market code. Returns matching contracts with their codes and categories. Use this to discover what COT data is available before calling GetCftcPositioning."
    )]
    public Task<string> SearchCftcMarkets(
        [Description(
            "Search query — market code or name keyword (e.g., 'gold', 'crude', 'S&P', '088691')"
        )]
            string query,
        [Description("Maximum number of results to return (default: 20)")] int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                // A negative maxResults would flow into .Take(...) as a negative SQL LIMIT,
                // which PostgreSQL rejects and surfaces as the internal-error sentinel. Clamp
                // so a non-positive cap yields zero rows and the existing no-results message.
                maxResults = Math.Max(0, maxResults);

                var contracts = await _contractRepository
                    .Search(query)
                    .OrderBy(c => c.Category)
                    .ThenBy(c => c.MarketName)
                    .Take(maxResults)
                    .ToListAsync();

                if (contracts.Count == 0)
                    return $"No contracts found matching '{query}'.";

                var result = MarkdownTable.Start(
                    $"CFTC contracts matching '{query}':",
                    "| Market Code | Name | Category |",
                    "|-------------|------|----------|"
                );

                foreach (var c in contracts)
                {
                    result.AppendLine(
                        $"| {c.MarketCode} | {c.MarketName} | {c.Category.NameForHumans()} |"
                    );
                }

                return result.ToString();
            },
            "SearchCftcMarkets",
            $"query: {query}"
        );
    }
}
