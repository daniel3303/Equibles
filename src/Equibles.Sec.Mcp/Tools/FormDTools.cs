using System.ComponentModel;
using System.Globalization;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Equibles.Sec.Data.Extensions;
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

    [McpServerTool(
        Name = "GetFormDOfferings",
        Title = "Exempt Offerings (Form D)",
        ReadOnly = true
    )]
    [Description(
        "Get recent exempt securities offerings (private placements) for a company from SEC Form D notices. Each Form D reports a Regulation D offering, showing the issuer, the date of first sale, the total offering amount (a dollar figure or \"Indefinite\"), the amounts sold and remaining, the minimum investment, the number of investors, the claimed exemptions, whether the notice is an amendment (D/A), and its SEC accession number. Ongoing offerings are re-noticed through D/A amendments that RESTATE the same offering — group rows by first-sale date and offering amount and use only the latest notice of each chain, or capital raised will be counted several times over. Use this to track how a company is raising private capital alongside its public filings."
    )]
    public Task<string> GetFormDOfferings(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description(
            "Maximum number of notices to return (default: 50, max: 500; values outside 1-500 are clamped)"
        )]
            int maxResults = 50,
        [Description(
            "Optional earliest filing date to include, ISO format yyyy-MM-dd (e.g., 2024-01-01)"
        )]
            string fromDate = null,
        [Description(
            "Optional latest filing date to include, ISO format yyyy-MM-dd (e.g., 2024-12-31)"
        )]
            string toDate = null,
        [Description("Number of matching notices to skip before returning rows (default: 0)")]
            int offset = 0
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var query = _formDRepository.GetByStock(stock);

                DateOnly? fromDay = null;
                DateOnly? toDay = null;

                if (!string.IsNullOrWhiteSpace(fromDate))
                {
                    if (!McpOutput.TryParseDate(fromDate, out var from))
                        return McpOutput.InvalidArgument("fromDate", fromDate, "yyyy-MM-dd");
                    fromDay = DateOnly.FromDateTime(from);
                }

                if (!string.IsNullOrWhiteSpace(toDate))
                {
                    if (!McpOutput.TryParseDate(toDate, out var to))
                        return McpOutput.InvalidArgument("toDate", toDate, "yyyy-MM-dd");
                    toDay = DateOnly.FromDateTime(to);
                }

                if (fromDay is { } parsedFrom && toDay is { } parsedTo && parsedFrom > parsedTo)
                    return "fromDate must be on or before toDate.";
                if (fromDay is { } earliestDay)
                    query = query.Where(f => f.FilingDate >= earliestDay);
                if (toDay is { } latestDay)
                    query = query.Where(f => f.FilingDate <= latestDay);

                var totalCount = await query.CountAsync();
                if (totalCount == 0)
                    return $"No Form D exempt offerings found for {ticker}.";

                offset = McpLimit.ClampOffset(offset);
                var filings = await query
                    .OrderNewestFirst()
                    .Skip(offset)
                    .Take(McpLimit.Clamp(maxResults))
                    .ToListAsync();
                if (filings.Count == 0 && offset > 0)
                    return $"No results at offset {offset} - only {totalCount} Form D notices match; lower offset.";

                var result = MarkdownTable.Start(
                    $"Recent exempt offerings (Form D) for {stock.Name} ({ticker}) — showing notices {offset + 1}-{offset + filings.Count} of {totalCount}, newest first:",
                    "| Filed | Amendment | First Sale | Industry | Offering Amount | Sold | Remaining | Min. Investment | Investors | Exemptions | Accession |",
                    "|-------|-----------|------------|----------|-----------------|------|-----------|-----------------|-----------|------------|-----------|"
                );

                result.AppendRows(
                    filings,
                    f =>
                        $"| {f.FilingDate:yyyy-MM-dd} | {(f.IsAmendment ? "Yes" : "No")} | {(f.DateOfFirstSale.HasValue ? f.DateOfFirstSale.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "-")} | {f.IndustryGroup ?? "-"} | {FormatAmount(f.TotalOfferingAmount, f.IsOfferingAmountIndefinite)} | ${McpFormat.WholeNumber(f.TotalAmountSold)} | {FormatAmount(f.TotalRemaining, f.IsRemainingIndefinite)} | ${McpFormat.WholeNumber(f.MinimumInvestmentAccepted)} | {McpFormat.WholeNumber(f.TotalNumberAlreadyInvested)} | {f.FederalExemptions ?? "-"} | {f.AccessionNumber ?? "-"} |"
                );

                if (filings.Any(f => f.IsAmendment))
                {
                    result.AppendLine();
                    result.AppendLine(
                        "_D/A amendments restate a prior notice for the same offering — group rows by first-sale date and offering amount; the latest notice of a chain supersedes the earlier ones, so do not sum Sold across a chain._"
                    );
                }

                var pagingNote = McpOutput.PagedTruncationNote(filings.Count, totalCount, offset);
                if (pagingNote.Length > 0)
                {
                    result.AppendLine();
                    result.AppendLine(pagingNote);
                }

                return result.ToString();
            },
            "GetFormDOfferings",
            $"ticker: {ticker}, fromDate: {fromDate}, toDate: {toDate}, offset: {offset}"
        );
    }

    private static string FormatAmount(long? amount, bool isIndefinite)
    {
        if (isIndefinite)
            return "Indefinite";
        return amount.HasValue ? $"${McpFormat.WholeNumber(amount.Value)}" : "-";
    }
}
