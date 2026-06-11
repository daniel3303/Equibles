using System.ComponentModel;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Repositories;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Congress.Mcp.Tools;

[McpServerToolType]
public class CongressTools
{
    private readonly CongressionalTradeRepository _tradeRepository;
    private readonly CongressMemberRepository _memberRepository;
    private readonly CongressionalAnnualDisclosureRepository _disclosureRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public CongressTools(
        CongressionalTradeRepository tradeRepository,
        CongressMemberRepository memberRepository,
        CongressionalAnnualDisclosureRepository disclosureRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<CongressTools> logger
    )
    {
        _tradeRepository = tradeRepository;
        _memberRepository = memberRepository;
        _disclosureRepository = disclosureRepository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetCongressionalTrades")]
    [Description(
        "Get congressional stock trades for a specific ticker. Shows which members of Congress bought or sold shares, transaction dates, and estimated amounts. Use SearchCongressMembers to find specific members."
    )]
    public Task<string> GetCongressionalTrades(
        [Description("Stock ticker symbol (e.g., AAPL, MSFT, NVDA)")] string ticker,
        [Description("Filter by transaction type: Purchase or Sale (defaults to all)")]
            string transactionType = null,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Maximum number of trades to return (default: 50, newest first)")]
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

                var query = _tradeRepository.GetByStock(stock, start, end);
                query = ApplyTransactionTypeFilter(query, transactionType);

                maxResults = McpLimit.Clamp(maxResults);

                var trades = await query
                    .Include(t => t.CongressMember)
                    .OrderByDescending(t => t.TransactionDate)
                    .Take(maxResults)
                    .ToListAsync();

                return MarkdownTable.Render(
                    trades,
                    $"No congressional trades found for {stock.Ticker} in the specified date range.",
                    $"Congressional trades for {stock.Ticker} ({stock.Name}):",
                    "| Date | Member | Position | Type | Amount Range | Owner |",
                    "|------|--------|----------|------|-------------|-------|",
                    t =>
                    {
                        var position = t.CongressMember.Position.NameForHumans();
                        var type = t.TransactionType.NameForHumans();
                        var amount = FormatAmountRange(t);
                        return $"| {t.TransactionDate:yyyy-MM-dd} | {t.CongressMember.Name} | {position} | {type} | {amount} | {t.OwnerType ?? "—"} |";
                    }
                );
            },
            "GetCongressionalTrades",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "GetMemberTrades")]
    [Description(
        "Get trading activity for a specific congress member. Shows all their stock trades with tickers, transaction types, and amounts. Use SearchCongressMembers to find member names."
    )]
    public Task<string> GetMemberTrades(
        [Description("Congress member name (e.g., 'Nancy Pelosi', 'Dan Crenshaw')")]
            string memberName,
        [Description("Filter by transaction type: Purchase or Sale (defaults to all)")]
            string transactionType = null,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")]
            string endDate = null,
        [Description("Maximum number of trades to return (default: 50, newest first)")]
            int maxResults = 50
    )
    {
        return _runner.Execute(
            async () =>
            {
                var member = await _memberRepository.GetByName(memberName.Trim());
                if (member == null)
                    return $"Member '{memberName}' not found. Use SearchCongressMembers to find the exact name.";

                var (start, end) = McpToolExecutor.ParseDateRange(
                    startDate,
                    endDate,
                    McpToolExecutor.UtcYearsAgo(1)
                );

                var query = _tradeRepository
                    .GetByMember(member)
                    .Where(t => t.TransactionDate >= start && t.TransactionDate <= end);
                query = ApplyTransactionTypeFilter(query, transactionType);

                var trades = await query
                    .Include(t => t.CommonStock)
                    .OrderByDescending(t => t.TransactionDate)
                    .Take(McpLimit.Clamp(maxResults))
                    .ToListAsync();

                return MarkdownTable.Render(
                    trades,
                    $"No trades found for {member.Name} ({member.Position.NameForHumans()}) in the specified date range.",
                    $"Trades by {member.Name} ({member.Position.NameForHumans()}):",
                    "| Date | Ticker | Type | Amount Range | Asset | Owner |",
                    "|------|--------|------|-------------|-------|-------|",
                    t =>
                    {
                        var type = t.TransactionType.NameForHumans();
                        var amount = FormatAmountRange(t);
                        return $"| {t.TransactionDate:yyyy-MM-dd} | {t.CommonStock.Ticker} | {type} | {amount} | {t.AssetName} | {t.OwnerType ?? "—"} |";
                    }
                );
            },
            "GetMemberTrades",
            $"memberName: {memberName}"
        );
    }

    [McpServerTool(Name = "GetMemberNetWorth")]
    [Description(
        "Get a congress member's net worth history from their annual financial disclosures. Disclosed values are ranges, so every year is a band (minimum-maximum), never a point estimate. Only electronically filed reports are read: a missing year means no electronic filing, not zero net worth."
    )]
    public Task<string> GetMemberNetWorth(
        [Description("Congress member name (e.g., 'Nancy Pelosi', 'Marsha Blackburn')")]
            string memberName,
        [Description("Maximum number of years to return (default: 20, newest first)")]
            int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                var member = await _memberRepository.GetByName(memberName.Trim());
                if (member == null)
                    return $"Member '{memberName}' not found. Use SearchCongressMembers to find the exact name.";

                var disclosures = await _disclosureRepository
                    .GetByMember(member)
                    .OrderByDescending(d => d.Year)
                    .Take(McpLimit.Clamp(maxResults))
                    .Select(d => new
                    {
                        d.Year,
                        d.FiledDate,
                        d.NetWorthMinimum,
                        d.NetWorthMaximum,
                        AssetCount = d.Lines.Count(l =>
                            l.Kind == CongressionalDisclosureLineKind.Asset
                        ),
                        LiabilityCount = d.Lines.Count(l =>
                            l.Kind == CongressionalDisclosureLineKind.Liability
                        ),
                    })
                    .ToListAsync();

                return MarkdownTable.Render(
                    disclosures,
                    $"No electronically filed annual disclosure found for {member.Name} ({member.Position.NameForHumans()}). Paper filings are not read, so this does not mean zero net worth.",
                    $"Net worth of {member.Name} ({member.Position.NameForHumans()}), as disclosed in annual financial reports (band = sum of disclosed asset minimums minus liability maximums, through asset maximums minus liability minimums):",
                    "| Year | Filed | Net Worth Minimum | Net Worth Maximum | Assets | Liabilities |",
                    "|------|-------|-------------------|-------------------|--------|-------------|",
                    d =>
                        $"| {d.Year} | {d.FiledDate:yyyy-MM-dd} | {FormatSignedAmount(d.NetWorthMinimum)} | {FormatSignedAmount(d.NetWorthMaximum)} | {d.AssetCount} | {d.LiabilityCount} |"
                );
            },
            "GetMemberNetWorth",
            $"memberName: {memberName}"
        );
    }

    // Net worth bands can be negative (liabilities exceeding assets).
    private static string FormatSignedAmount(long value) =>
        value < 0 ? $"-${McpFormat.WholeNumber(-value)}" : $"${McpFormat.WholeNumber(value)}";

    [McpServerTool(Name = "SearchCongressMembers")]
    [Description(
        "Search for members of Congress by name. Returns matching members with their position (Senator/Representative). Use this to discover member names before calling GetMemberTrades."
    )]
    public Task<string> SearchCongressMembers(
        [Description("Search query — partial or full name (e.g., 'Pelosi', 'Cruz', 'Dan')")]
            string query,
        [Description("Maximum number of results to return (default: 20)")] int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                maxResults = McpLimit.Clamp(maxResults);

                var members = await _memberRepository
                    .Search(query.Trim())
                    .OrderBy(m => m.Name)
                    .Take(maxResults)
                    .ToListAsync();

                return MarkdownTable.Render(
                    members,
                    $"No congress members found matching '{query}'.",
                    $"Congress members matching '{query}':",
                    "| Name | Position |",
                    "|------|----------|",
                    m => $"| {m.Name} | {m.Position.NameForHumans()} |"
                );
            },
            "SearchCongressMembers",
            $"query: {query}"
        );
    }

    // Format with InvariantCulture so the MCP markdown does not fork the separators by host
    // locale (e.g. de-DE would render $1.000.000).
    private static string FormatAmountRange(CongressionalTrade t) =>
        $"${McpFormat.WholeNumber(t.AmountFrom)}–${McpFormat.WholeNumber(t.AmountTo)}";

    private static IQueryable<CongressionalTrade> ApplyTransactionTypeFilter(
        IQueryable<CongressionalTrade> query,
        string transactionType
    )
    {
        if (
            string.IsNullOrEmpty(transactionType)
            || !Enum.TryParse<CongressTransactionType>(transactionType, true, out var parsedType)
        )
        {
            return query;
        }
        return query.Where(t => t.TransactionType == parsedType);
    }
}
