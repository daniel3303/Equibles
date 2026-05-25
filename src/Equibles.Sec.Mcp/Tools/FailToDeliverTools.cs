using System.ComponentModel;
using System.Text;
using Equibles.CommonStocks.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Sec.Mcp.Tools;

[McpServerToolType]
public class FailToDeliverTools
{
    private readonly FailToDeliverRepository _ftdRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public FailToDeliverTools(
        FailToDeliverRepository ftdRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<FailToDeliverTools> logger
    )
    {
        _ftdRepository = ftdRepository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(
            logger,
            (tool, msg, stack, ctx) =>
                errorManager.Create(ErrorSource.McpTool, tool, msg, stack, ctx)
        );
    }

    [McpServerTool(Name = "GetFailsToDeliver")]
    [Description(
        "Get fails-to-deliver (FTD) data for a stock from SEC. Shows settlement dates, quantities of shares that failed to deliver, and price at settlement. High FTD counts may indicate naked short selling or settlement issues."
    )]
    public Task<string> GetFailsToDeliver(
        [Description("Stock ticker symbol (e.g., AAPL, GME, AMC)")] string ticker,
        [Description("Start date in YYYY-MM-DD format (defaults to 3 months ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Maximum number of records to return (default: 90, newest first)")]
            int maxResults = 90
    )
    {
        return _runner.Execute(
            async () =>
            {
                var stock = await _commonStockRepository.GetByTicker(
                    ticker.Trim().ToUpperInvariant()
                );
                if (stock == null)
                    return $"Stock '{ticker}' not found.";

                var start = McpToolExecutor.ParseDateOr(
                    startDate,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3))
                );
                var end = McpToolExecutor.ParseDateOr(
                    endDate,
                    DateOnly.FromDateTime(DateTime.UtcNow)
                );

                var records = await _ftdRepository
                    .GetByStock(stock)
                    .Where(f => f.SettlementDate >= start && f.SettlementDate <= end)
                    .OrderByDescending(f => f.SettlementDate)
                    .Take(maxResults)
                    .ToListAsync();

                if (records.Count == 0)
                    return $"No FTD data found for {stock.Ticker} in the specified date range.";

                var result = new StringBuilder();
                result.AppendLine($"Fails-to-deliver for {stock.Ticker} ({stock.Name}):");
                result.AppendLine();
                result.AppendLine("| Settlement Date | Quantity | Price | Value |");
                result.AppendLine("|----------------|---------|-------|-------|");

                foreach (var f in records.OrderBy(f => f.SettlementDate))
                {
                    var value = f.Quantity * f.Price;
                    result.AppendLine(
                        $"| {f.SettlementDate:yyyy-MM-dd} | {f.Quantity:N0} | ${f.Price:F2} | ${value:N0} |"
                    );
                }

                return result.ToString();
            },
            "GetFailsToDeliver",
            $"ticker: {ticker}"
        );
    }
}
