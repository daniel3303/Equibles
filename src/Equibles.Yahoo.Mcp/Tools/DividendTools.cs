using System.ComponentModel;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
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
    private readonly EquityIssuerRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public DividendTools(
        CashDividendRepository cashDividendRepository,
        EquityIssuerRepository commonStockRepository,
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
        "Get the stored declared cash dividends of an exact stock or ETF listing, newest first. Each row gives the ex-dividend date and cash amount per share: in USD for a US listing, and in the listing's trading currency for a listing on another exchange (MIC:TICKER). Date filters apply to the ex-dividend date. Future ex-dates can appear after a dividend is declared. Each listing reports its own payments: a share class (BRK-A beside BRK-B) or a fund series sharing its trust's filer (SCHD beside FNDA) never inherits another listing's dividends."
    )]
    public Task<string> GetDividendHistory(
        [Description(
            "Listed stock or ETF ticker (e.g., AAPL, BRK-A, SCHD); a listing on another exchange is MIC:TICKER (XPAR:AIR)."
        )]
            string ticker,
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

                var (listing, label, venue, error) = await ResolveDividendListing(ticker);
                if (listing == null)
                    return error;

                var start = startDate.HasValue
                    ? DateOnly.FromDateTime(startDate.Value)
                    : (DateOnly?)null;
                var end = endDate.HasValue ? DateOnly.FromDateTime(endDate.Value) : (DateOnly?)null;
                maxResults = McpLimit.Clamp(maxResults);
                offset = McpLimit.ClampOffset(offset);

                // A venue declares in its own currency; a US listing keeps its USD-only contract.
                var query = _cashDividendRepository.GetHistoryByListing(listing.Id, start, end);
                if (!venue)
                    query = query.Where(dividend => dividend.Currency == "USD");
                var total = await query.CountAsync();
                var dividends = await query.Skip(offset).Take(maxResults).ToListAsync();

                if (dividends.Count == 0)
                {
                    if (total > 0)
                        return McpOutput.PagedTruncationNote(0, total, offset);

                    return start.HasValue || end.HasValue
                        ? $"No stored cash-dividend records match the ex-date range for {EquityListingSymbol.Display(listing)}."
                        : $"No cash-dividend records are stored for {EquityListingSymbol.Display(listing)}.";
                }

                var result = MarkdownTable.Start(
                    $"Declared cash dividends for {label}, newest first:",
                    "Ex-Date | Amount Per Share",
                    "--------|-----------------"
                );
                result.AppendRows(
                    dividends,
                    dividend =>
                        venue
                            ? $"{McpFormat.Invariant(dividend.ExDate, "yyyy-MM-dd")} | {McpFormat.Price(dividend.AmountPerShare)} {dividend.Currency}"
                            : $"{McpFormat.Invariant(dividend.ExDate, "yyyy-MM-dd")} | ${McpFormat.Price(dividend.AmountPerShare)}"
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

    // A bare ticker is the exact US listing; MIC:TICKER, or a provider suffix once the US lookup
    // misses, is one venue listing.
    private async Task<(
        EquityListing Listing,
        string Label,
        bool Venue,
        string Error
    )> ResolveDividendListing(string ticker)
    {
        if (!EquityListingSymbol.TryParse(ticker, out _, out _))
        {
            var normalizedTicker = TickerNormalizer.NormalizeDashListed(ticker);
            if (normalizedTicker != null)
            {
                EquityIssuer stock = await _commonStockRepository.GetUsByTicker(normalizedTicker);
                var usListing = SecondaryTickerPolicy.ResolveExactUsListing(
                    stock,
                    normalizedTicker
                );
                if (usListing != null)
                {
                    // The filer's name can describe a different fund series, so only its presentation listing carries it.
                    var usLabel =
                        usListing.Id == stock.Presentation?.EquityListingId
                            ? $"{MarkdownTable.EscapeCell(stock.Name)} ({MarkdownTable.EscapeCell(usListing.Ticker)})"
                            : MarkdownTable.EscapeCell(usListing.Ticker);
                    return (usListing, usLabel, false, null);
                }
            }
            if (ticker == null || !ticker.Contains('.'))
                return (null, null, false, McpToolExecutor.StockNotFound(ticker));
        }

        var (venueStock, venue, error) = await _commonStockRepository.ResolveListing(ticker);
        if (venue == null)
            return (
                null,
                null,
                false,
                EquityListingSymbol.TryParse(ticker, out _, out _)
                    ? error
                    : McpToolExecutor.StockNotFound(ticker)
            );
        return (
            venue,
            $"{MarkdownTable.EscapeCell(venueStock.Name)} ({MarkdownTable.EscapeCell(EquityListingSymbol.Display(venue))})",
            true,
            null
        );
    }
}
