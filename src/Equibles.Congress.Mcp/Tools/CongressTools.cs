using System.ComponentModel;
using System.Text;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Repositories;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Congress.Mcp.Tools;

[McpServerToolType]
public class CongressTools {
    private readonly CongressionalTradeRepository _tradeRepository;
    private readonly CongressMemberRepository _memberRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly ErrorManager _errorManager;
    private readonly ILogger<CongressTools> _logger;

    public CongressTools(
        CongressionalTradeRepository tradeRepository,
        CongressMemberRepository memberRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<CongressTools> logger
    ) {
        _tradeRepository = tradeRepository;
        _memberRepository = memberRepository;
        _commonStockRepository = commonStockRepository;
        _errorManager = errorManager;
        _logger = logger;
    }

    [McpServerTool(Name = "GetCongressionalTrades")]
    [Description("Get congressional stock trades for a specific ticker. Shows which members of Congress bought or sold shares, transaction dates, and estimated amounts. Use SearchCongressMembers to find specific members.")]
    public Task<string> GetCongressionalTrades(
        [Description("Stock ticker symbol (e.g., AAPL, MSFT, NVDA)")] string ticker,
        [Description("Filter by transaction type: Purchase or Sale (defaults to all)")] string transactionType = null,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")] string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")] string endDate = null,
        [Description("Maximum number of trades to return (default: 50, newest first)")] int maxResults = 50
    ) {
        return McpToolExecutor.Execute(async () => {
            var stock = await _commonStockRepository.GetByTicker(ticker.Trim().ToUpperInvariant());
            if (stock == null) return $"Stock '{ticker}' not found.";

            var start = !string.IsNullOrEmpty(startDate) && DateOnly.TryParse(startDate, out var parsedStart)
                ? parsedStart
                : DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));

            var end = !string.IsNullOrEmpty(endDate) && DateOnly.TryParse(endDate, out var parsedEnd)
                ? parsedEnd
                : DateOnly.FromDateTime(DateTime.UtcNow);

            var query = _tradeRepository.GetByStock(stock, start, end);

            if (!string.IsNullOrEmpty(transactionType) && Enum.TryParse<CongressTransactionType>(transactionType, true, out var parsedType)) {
                query = query.Where(t => t.TransactionType == parsedType);
            }

            var trades = await query
                .Include(t => t.CongressMember)
                .OrderByDescending(t => t.TransactionDate)
                .Take(maxResults)
                .ToListAsync();

            if (trades.Count == 0) return $"No congressional trades found for {stock.Ticker} in the specified date range.";

            var result = new StringBuilder();
            result.AppendLine($"Congressional trades for {stock.Ticker} ({stock.Name}):");
            result.AppendLine();
            result.AppendLine("| Date | Member | Position | Type | Amount Range | Owner |");
            result.AppendLine("|------|--------|----------|------|-------------|-------|");

            foreach (var t in trades) {
                var position = t.CongressMember.Position.NameForHumans();
                var type = t.TransactionType.NameForHumans();
                var amount = $"${t.AmountFrom:N0}–${t.AmountTo:N0}";
                result.AppendLine($"| {t.TransactionDate:yyyy-MM-dd} | {t.CongressMember.Name} | {position} | {type} | {amount} | {t.OwnerType ?? "—"} |");
            }

            return result.ToString();
        }, _logger, "GetCongressionalTrades", $"ticker: {ticker}", ReportError);
    }

    [McpServerTool(Name = "GetMemberTrades")]
    [Description("Get trading activity for a specific congress member. Shows all their stock trades with tickers, transaction types, and amounts. Use SearchCongressMembers to find member names.")]
    public Task<string> GetMemberTrades(
        [Description("Congress member name (e.g., 'Nancy Pelosi', 'Dan Crenshaw')")] string memberName,
        [Description("Filter by transaction type: Purchase or Sale (defaults to all)")] string transactionType = null,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")] string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to latest available)")] string endDate = null,
        [Description("Maximum number of trades to return (default: 50, newest first)")] int maxResults = 50
    ) {
        return McpToolExecutor.Execute(async () => {
            var member = await _memberRepository.GetByName(memberName.Trim());
            if (member == null) return $"Member '{memberName}' not found. Use SearchCongressMembers to find the exact name.";

            var start = !string.IsNullOrEmpty(startDate) && DateOnly.TryParse(startDate, out var parsedStart)
                ? parsedStart
                : DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));

            var end = !string.IsNullOrEmpty(endDate) && DateOnly.TryParse(endDate, out var parsedEnd)
                ? parsedEnd
                : DateOnly.FromDateTime(DateTime.UtcNow);

            var query = _tradeRepository.GetByMember(member)
                .Where(t => t.TransactionDate >= start && t.TransactionDate <= end);

            if (!string.IsNullOrEmpty(transactionType) && Enum.TryParse<CongressTransactionType>(transactionType, true, out var parsedType)) {
                query = query.Where(t => t.TransactionType == parsedType);
            }

            var trades = await query
                .Include(t => t.CommonStock)
                .OrderByDescending(t => t.TransactionDate)
                .Take(maxResults)
                .ToListAsync();

            if (trades.Count == 0) return $"No trades found for {member.Name} ({member.Position.NameForHumans()}) in the specified date range.";

            var result = new StringBuilder();
            result.AppendLine($"Trades by {member.Name} ({member.Position.NameForHumans()}):");
            result.AppendLine();
            result.AppendLine("| Date | Ticker | Type | Amount Range | Asset | Owner |");
            result.AppendLine("|------|--------|------|-------------|-------|-------|");

            foreach (var t in trades) {
                var type = t.TransactionType.NameForHumans();
                var amount = $"${t.AmountFrom:N0}–${t.AmountTo:N0}";
                result.AppendLine($"| {t.TransactionDate:yyyy-MM-dd} | {t.CommonStock.Ticker} | {type} | {amount} | {t.AssetName} | {t.OwnerType ?? "—"} |");
            }

            return result.ToString();
        }, _logger, "GetMemberTrades", $"memberName: {memberName}", ReportError);
    }

    [McpServerTool(Name = "SearchCongressMembers")]
    [Description("Search for members of Congress by name. Returns matching members with their position (Senator/Representative). Use this to discover member names before calling GetMemberTrades.")]
    public Task<string> SearchCongressMembers(
        [Description("Search query — partial or full name (e.g., 'Pelosi', 'Cruz', 'Dan')")] string query,
        [Description("Maximum number of results to return (default: 20)")] int maxResults = 20
    ) {
        return McpToolExecutor.Execute(async () => {
            var members = await _memberRepository.Search(query.Trim())
                .OrderBy(m => m.Name)
                .Take(maxResults)
                .ToListAsync();

            if (members.Count == 0) return $"No congress members found matching '{query}'.";

            var result = new StringBuilder();
            result.AppendLine($"Congress members matching '{query}':");
            result.AppendLine();
            result.AppendLine("| Name | Position |");
            result.AppendLine("|------|----------|");

            foreach (var m in members) {
                result.AppendLine($"| {m.Name} | {m.Position.NameForHumans()} |");
            }

            return result.ToString();
        }, _logger, "SearchCongressMembers", $"query: {query}", ReportError);
    }

    private Task ReportError(string toolName, string message, string stackTrace, string context) {
        return _errorManager.Create(ErrorSource.McpTool, toolName, message, stackTrace, context);
    }
}
