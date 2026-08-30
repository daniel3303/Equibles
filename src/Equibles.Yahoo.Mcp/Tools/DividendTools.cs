using System.ComponentModel;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Yahoo.Mcp.Tools;

[McpServerToolType]
public class DividendTools
{
    private readonly CashDividendRepository _cashDividendRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public DividendTools(
        CashDividendRepository cashDividendRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<DividendTools> logger
    )
    {
        _cashDividendRepository = cashDividendRepository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetDividendHistory", Title = "Dividend History", ReadOnly = true)]
    [Description(
        "Get a company's stored declared cash dividends newest first. Each row gives the ex-dividend date, cash amount per share in USD, and source. Date filters apply to the ex-dividend date. Future ex-dates can appear after a dividend is declared. Dividend records are issuer-level and available only through the company's current primary ticker; a secondary share class is never assumed to have the same dividend."
    )]
    public Task<string> GetDividendHistory(
        [Description("Current primary stock ticker (e.g., AAPL, MSFT).")] string ticker,
        [Description("Optional earliest ex-dividend date in YYYY-MM-DD format.")]
            DateTime? startDate = null,
        [Description("Optional latest ex-dividend date in YYYY-MM-DD format.")]
            DateTime? endDate = null,
        [Description("Maximum number of records to return (default: 20, max: 500).")]
            int maxResults = 20,
        [Description("Number of newest matching records to skip for pagination (default: 0).")]
            int offset = 0
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
                {
                    return $"startDate {McpFormat.Invariant(startDate.Value, "yyyy-MM-dd")} is after endDate {McpFormat.Invariant(endDate.Value, "yyyy-MM-dd")} — swap the values.";
                }

                var normalizedTicker = TickerNormalizer.NormalizeDashListed(ticker);
                if (normalizedTicker == null)
                    return McpToolExecutor.StockNotFound(ticker);

                var stock = await _commonStockRepository.GetByTicker(normalizedTicker);
                if (stock == null)
                    return McpToolExecutor.StockNotFound(ticker);
                if (!string.Equals(stock.Ticker, normalizedTicker, StringComparison.Ordinal))
                {
                    return $"Dividend history is available only for the current primary ticker {stock.Ticker}; {normalizedTicker} is a separate listing and is not assumed to share its dividends.";
                }

                var start = startDate.HasValue
                    ? DateOnly.FromDateTime(startDate.Value)
                    : (DateOnly?)null;
                var end = endDate.HasValue ? DateOnly.FromDateTime(endDate.Value) : (DateOnly?)null;
                maxResults = McpLimit.Clamp(maxResults);
                offset = McpLimit.ClampOffset(offset);

                var query = _cashDividendRepository.GetHistory(stock.Id, start, end);
                var total = await query.CountAsync();
                var dividends = await query.Skip(offset).Take(maxResults).ToListAsync();

                if (dividends.Count == 0)
                {
                    if (total > 0)
                        return McpOutput.PagedTruncationNote(0, total, offset);

                    return start.HasValue || end.HasValue
                        ? $"No stored cash-dividend records match the ex-date range for {stock.Ticker}."
                        : $"No cash-dividend records are stored for {stock.Ticker}.";
                }

                var result = MarkdownTable.Start(
                    $"Declared cash dividends for {MarkdownTable.EscapeCell(stock.Name)} ({MarkdownTable.EscapeCell(stock.Ticker)}), newest first:",
                    "Ex-Date | Amount Per Share | Source",
                    "--------|------------------|-------"
                );
                result.AppendRows(
                    dividends,
                    dividend =>
                        $"{McpFormat.Invariant(dividend.ExDate, "yyyy-MM-dd")} | ${McpFormat.Price(dividend.AmountPerShare)} | {dividend.Source}"
                );

                var pagingNote = McpOutput.PagedTruncationNote(dividends.Count, total, offset);
                if (!string.IsNullOrEmpty(pagingNote))
                {
                    result.AppendLine();
                    result.AppendLine(pagingNote);
                }

                return result.ToString();
            },
            "GetDividendHistory",
            $"ticker: {ticker}"
        );
    }
}
