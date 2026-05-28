using System.ComponentModel;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
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
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
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
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var (start, end) = McpToolExecutor.ParseDateRange(
                    startDate,
                    endDate,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3))
                );

                var records = await _ftdRepository
                    .GetByStock(stock)
                    .Where(f => f.SettlementDate >= start && f.SettlementDate <= end)
                    .OrderByDescending(f => f.SettlementDate)
                    .Take(maxResults)
                    .ToListAsync();

                if (records.Count == 0)
                    return $"No FTD data found for {stock.Ticker} in the specified date range.";

                var result = MarkdownTable.Start(
                    $"Fails-to-deliver for {stock.Ticker} ({stock.Name}):",
                    "| Settlement Date | Quantity | Price | Value |",
                    "|----------------|---------|-------|-------|"
                );

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
