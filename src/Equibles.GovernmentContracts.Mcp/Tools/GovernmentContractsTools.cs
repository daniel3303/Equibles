using System.ComponentModel;
using System.Globalization;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.GovernmentContracts.Data.Models;
using Equibles.GovernmentContracts.Repositories;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.GovernmentContracts.Mcp.Tools;

[McpServerToolType]
public class GovernmentContractsTools
{
    private readonly GovernmentContractRepository _contractRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public GovernmentContractsTools(
        GovernmentContractRepository contractRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<GovernmentContractsTools> logger
    )
    {
        _contractRepository = contractRepository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetGovernmentContracts")]
    [Description(
        "Get federal government contract awards (from USAspending.gov) won by a specific public company. "
            + "Shows the awarding agency, award amount, period dates, and description — useful for gauging a "
            + "company's reliance on federal spending."
    )]
    public Task<string> GetGovernmentContracts(
        [Description("Stock ticker symbol (e.g., LMT, RTX, BA)")] string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Maximum number of awards to return (default: 50, largest first)")]
            int maxResults = 50
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var (start, end) = McpToolExecutor.ParseDateRange(
                    startDate,
                    endDate,
                    McpToolExecutor.UtcYearsAgo(1)
                );
                maxResults = McpLimit.Clamp(maxResults);

                var awards = await _contractRepository
                    .GetByCommonStock(stock)
                    .Where(c => c.ActionDate >= start && c.ActionDate <= end)
                    .OrderByDescending(c => c.Amount)
                    .Take(maxResults)
                    .ToListAsync();

                return MarkdownTable.Render(
                    awards,
                    $"No federal contract awards found for {stock.Ticker} in the specified date range.",
                    $"Federal contract awards for {stock.Ticker} ({stock.Name}):",
                    "| Action Date | Agency | Type | Amount | Award ID | Description |",
                    "|-------------|--------|------|--------|----------|-------------|",
                    c =>
                        $"| {Format(c.ActionDate)} | {Escape(c.AwardingAgency)} | {AwardTypeLabel(c.AwardType)} "
                        + $"| {FormatUsd(c.Amount)} | {Escape(c.AwardId)} | {Escape(Shorten(c.Description, 80))} |"
                );
            },
            "GetGovernmentContracts",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "GetTopGovernmentContractors")]
    [Description(
        "Rank public companies by total federal contract dollars awarded over a date range (from "
            + "USAspending.gov). Answers questions like 'which public companies won the most federal contracts "
            + "last quarter'."
    )]
    public Task<string> GetTopGovernmentContractors(
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Maximum number of companies to return (default: 25, largest first)")]
            int maxResults = 25
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (start, end) = McpToolExecutor.ParseDateRange(
                    startDate,
                    endDate,
                    McpToolExecutor.UtcYearsAgo(1)
                );
                maxResults = McpLimit.Clamp(maxResults);

                var ranked = await _contractRepository
                    .GetAll()
                    .Where(c => c.ActionDate >= start && c.ActionDate <= end)
                    .GroupBy(c => new
                    {
                        c.CommonStockId,
                        c.CommonStock.Ticker,
                        c.CommonStock.Name,
                    })
                    .Select(g => new
                    {
                        g.Key.Ticker,
                        g.Key.Name,
                        Total = g.Sum(c => c.Amount),
                        Count = g.Count(),
                    })
                    .OrderByDescending(r => r.Total)
                    .Take(maxResults)
                    .ToListAsync();

                var rank = 0;
                return MarkdownTable.Render(
                    ranked,
                    "No federal contract awards found in the specified date range.",
                    $"Top federal contractors ({start:yyyy-MM-dd} to {end:yyyy-MM-dd}):",
                    "| Rank | Ticker | Company | Total Awarded | Awards |",
                    "|------|--------|---------|---------------|--------|",
                    r =>
                        $"| {++rank} | {Escape(r.Ticker)} | {Escape(r.Name)} | {FormatUsd(r.Total)} | {r.Count} |"
                );
            },
            "GetTopGovernmentContractors",
            $"range: {startDate}..{endDate}"
        );
    }

    private static string AwardTypeLabel(GovernmentContractAwardType type) =>
        type switch
        {
            GovernmentContractAwardType.BpaCall => "BPA Call",
            GovernmentContractAwardType.PurchaseOrder => "Purchase Order",
            GovernmentContractAwardType.DeliveryOrder => "Delivery Order",
            GovernmentContractAwardType.DefinitiveContract => "Definitive Contract",
            _ => "Unknown",
        };

    private static string FormatUsd(decimal amount) =>
        "$" + amount.ToString("N0", CultureInfo.InvariantCulture);

    private static string Format(DateOnly? date) =>
        date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";

    private static string Shorten(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
            return trimmed;
        return trimmed.TruncateToFit(maxLength) + "…";
    }

    // Markdown cells can't contain a raw pipe or newline without breaking the table.
    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }
}
