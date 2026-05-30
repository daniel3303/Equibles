using System.ComponentModel;
using System.Globalization;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
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
public class FormDTools
{
    private readonly FormDFilingRepository _formDRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public FormDTools(
        FormDFilingRepository formDRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<FormDTools> logger
    )
    {
        _formDRepository = formDRepository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetExemptOfferings")]
    [Description(
        "Get recent exempt securities offerings (private placements) for a company from SEC Form D notices. Each Form D reports a Regulation D offering, showing the issuer, the total offering amount and amount sold so far (either a dollar figure or \"Indefinite\"), the minimum investment, the number of investors, the claimed exemptions, and whether the notice is an amendment. Use this to track how a company is raising private capital alongside its public filings."
    )]
    public Task<string> GetExemptOfferings(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Maximum number of notices to return (default: 50)")] int maxResults = 50
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var filings = await _formDRepository
                    .GetByStock(stock)
                    .OrderByDescending(f => f.FilingDate)
                    .Take(maxResults)
                    .ToListAsync();

                if (filings.Count == 0)
                    return $"No Form D exempt offerings found for {ticker}.";

                var result = MarkdownTable.Start(
                    $"Recent exempt offerings (Form D) for {stock.Name} ({ticker}) — showing {filings.Count} most recent notices:",
                    "| Filed | Amendment | Industry | Offering Amount | Sold | Min. Investment | Investors | Exemptions |",
                    "|-------|-----------|----------|-----------------|------|-----------------|-----------|------------|"
                );

                foreach (var f in filings)
                {
                    result.AppendLine(
                        $"| {f.FilingDate:yyyy-MM-dd} | {(f.IsAmendment ? "Yes" : "No")} | {f.IndustryGroup ?? "-"} | {FormatAmount(f.TotalOfferingAmount, f.IsOfferingAmountIndefinite)} | ${FormatWholeNumber(f.TotalAmountSold)} | ${FormatWholeNumber(f.MinimumInvestmentAccepted)} | {FormatWholeNumber(f.TotalNumberAlreadyInvested)} | {f.FederalExemptions ?? "-"} |"
                    );
                }

                return result.ToString();
            },
            "GetExemptOfferings",
            $"ticker: {ticker}"
        );
    }

    private static string FormatAmount(long? amount, bool isIndefinite)
    {
        if (isIndefinite)
            return "Indefinite";
        return amount.HasValue ? $"${FormatWholeNumber(amount.Value)}" : "-";
    }

    private static string FormatWholeNumber(long value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);
}
