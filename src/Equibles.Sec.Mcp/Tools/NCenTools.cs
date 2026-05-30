using System.ComponentModel;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.Extensions;
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
public class NCenTools
{
    private readonly NCenFilingRepository _nCenRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public NCenTools(
        NCenFilingRepository nCenRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<NCenTools> logger
    )
    {
        _nCenRepository = nCenRepository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetFundOperations")]
    [Description(
        "Get operational data for a registered investment company (mutual fund, ETF or closed-end fund) from its SEC Form N-CEN annual reports. Each N-CEN shows the registrant's classification (e.g. N-1A open-end, N-2 closed-end), Investment Company Act file number, reporting period, and whether it was the fund's first or last filing, followed by the fund's named service providers — investment advisers, sub-advisers, custodians, transfer agents, administrators, auditors and underwriters. Use this to see who runs and services a fund. Only registered funds file N-CEN; operating companies will return no data."
    )]
    public Task<string> GetFundOperations(
        [Description("Fund or ETF ticker symbol (e.g., MXF, SPY)")] string ticker,
        [Description("Maximum number of annual reports to return (default: 10)")]
            int maxResults = 10
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var filings = await _nCenRepository
                    .GetByStock(stock)
                    .Include(f => f.ServiceProviders)
                    .OrderByDescending(f => f.FilingDate)
                    .Take(maxResults)
                    .ToListAsync();

                if (filings.Count == 0)
                    return $"No Form N-CEN annual reports found for {ticker}.";

                var result = MarkdownTable.Start(
                    $"Form N-CEN annual reports for {stock.Name} ({ticker}) — showing {filings.Count} most recent:",
                    "| Filed | Period End | Type | File Number | Amendment | First Filing | Last Filing |",
                    "|-------|------------|------|-------------|-----------|--------------|-------------|"
                );

                foreach (var f in filings)
                {
                    result.AppendLine(
                        $"| {f.FilingDate:yyyy-MM-dd} | {f.ReportEndingPeriod:yyyy-MM-dd} | {f.InvestmentCompanyType ?? "-"} | {f.InvestmentCompanyFileNumber ?? "-"} | {(f.IsAmendment ? "Yes" : "No")} | {(f.IsFirstFiling ? "Yes" : "No")} | {(f.IsLastFiling ? "Yes" : "No")} |"
                    );
                }

                AppendServiceProviders(result, filings[0]);

                return result.ToString();
            },
            "GetFundOperations",
            $"ticker: {ticker}"
        );
    }

    private static void AppendServiceProviders(System.Text.StringBuilder result, NCenFiling latest)
    {
        if (latest.ServiceProviders.Count == 0)
            return;

        result.AppendLine();
        result.AppendLine(
            $"Service providers reported on the latest report (filed {latest.FilingDate:yyyy-MM-dd}):"
        );
        result.AppendLine();
        result.AppendLine("| Role | Firm | Country | Affiliated |");
        result.AppendLine("|------|------|---------|------------|");

        foreach (
            var provider in latest.ServiceProviders.OrderBy(p => p.ProviderType).ThenBy(p => p.Name)
        )
        {
            result.AppendLine(
                $"| {provider.ProviderType.NameForHumans()} | {provider.Name} | {provider.Country ?? "-"} | {(provider.IsAffiliated ? "Yes" : "No")} |"
            );
        }
    }
}
