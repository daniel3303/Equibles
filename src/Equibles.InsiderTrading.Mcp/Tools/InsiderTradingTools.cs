using System.ComponentModel;
using System.Text;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.InsiderTrading.Mcp.Tools;

[McpServerToolType]
public class InsiderTradingTools {
    private readonly InsiderTransactionRepository _transactionRepository;
    private readonly InsiderOwnerRepository _ownerRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly ErrorManager _errorManager;
    private readonly ILogger<InsiderTradingTools> _logger;

    public InsiderTradingTools(
        InsiderTransactionRepository transactionRepository,
        InsiderOwnerRepository ownerRepository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<InsiderTradingTools> logger
    ) {
        _transactionRepository = transactionRepository;
        _ownerRepository = ownerRepository;
        _commonStockRepository = commonStockRepository;
        _errorManager = errorManager;
        _logger = logger;
    }

    [McpServerTool(Name = "GetInsiderTransactions")]
    [Description("Get recent insider trading transactions (purchases, sales, awards) for a stock from SEC Form 3 and Form 4 filings. Shows insider name, role, transaction type, shares, price, and post-transaction ownership. Use this to understand insider buying/selling activity.")]
    public Task<string> GetInsiderTransactions(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Maximum number of transactions to return (default: 50)")] int maxResults = 50
    ) {
        return McpToolExecutor.Execute(async () => {
            var stock = await _commonStockRepository.GetByTicker(ticker);
            if (stock == null) return $"Stock '{ticker}' not found.";

            var transactions = await _transactionRepository.GetByStock(stock)
                .Include(t => t.InsiderOwner)
                .OrderByDescending(t => t.TransactionDate)
                .Take(maxResults)
                .ToListAsync();

            if (transactions.Count == 0)
                return $"No insider transactions found for {ticker}.";

            var result = new StringBuilder();
            result.AppendLine($"Recent insider transactions for {stock.Name} ({ticker}):");
            result.AppendLine($"Showing {transactions.Count} most recent transactions");
            result.AppendLine();
            result.AppendLine("| Date | Insider | Role | Type | Shares | Price | Value | Owned After |");
            result.AppendLine("|------|---------|------|------|--------|-------|-------|-------------|");

            foreach (var t in transactions) {
                var role = GetRole(t.InsiderOwner);
                var type = t.AcquiredDisposed == AcquiredDisposed.Acquired ? "Buy" : "Sell";
                if (t.TransactionCode == TransactionCode.Award) type = "Award";
                if (t.TransactionCode == TransactionCode.Gift) type = "Gift";
                if (t.TransactionCode == TransactionCode.Exercise) type = "Exercise";

                var value = t.Shares * t.PricePerShare;
                result.AppendLine(
                    $"| {t.TransactionDate:yyyy-MM-dd} | {t.InsiderOwner.Name} | {role} | {type} | {t.Shares:N0} | ${t.PricePerShare:N2} | ${value:N0} | {t.SharesOwnedAfter:N0} |");
            }

            return result.ToString();
        }, _logger, "GetInsiderTransactions", $"ticker: {ticker}", ReportError);
    }

    [McpServerTool(Name = "GetInsiderOwnership")]
    [Description("Get a summary of current insider ownership for a stock. Shows each insider, their role, total shares held, and their most recent transaction. Use this to understand the insider ownership structure of a company.")]
    public Task<string> GetInsiderOwnership(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker
    ) {
        return McpToolExecutor.Execute(async () => {
            var stock = await _commonStockRepository.GetByTicker(ticker);
            if (stock == null) return $"Stock '{ticker}' not found.";

            var byStock = _transactionRepository.GetByStock(stock);

            var latestTransactions = await byStock
                .Where(t => t.Id == byStock
                    .Where(t2 => t2.InsiderOwnerId == t.InsiderOwnerId)
                    .OrderByDescending(t2 => t2.TransactionDate)
                    .ThenByDescending(t2 => t2.FilingDate)
                    .Select(t2 => t2.Id)
                    .First())
                .Include(t => t.InsiderOwner)
                .OrderByDescending(t => t.SharesOwnedAfter)
                .Take(30)
                .ToListAsync();

            if (latestTransactions.Count == 0)
                return $"No insider ownership data found for {ticker}.";

            var result = new StringBuilder();
            result.AppendLine($"Insider ownership summary for {stock.Name} ({ticker}):");
            result.AppendLine($"Showing {latestTransactions.Count} insiders with most recent data");
            result.AppendLine();
            result.AppendLine("| Insider | Role | Shares Owned | Last Transaction | Last Date |");
            result.AppendLine("|---------|------|-------------|-----------------|-----------|");

            foreach (var t in latestTransactions) {
                var role = GetRole(t.InsiderOwner);
                var lastType = t.TransactionCode.ToString();
                result.AppendLine(
                    $"| {t.InsiderOwner.Name} | {role} | {t.SharesOwnedAfter:N0} | {lastType} | {t.TransactionDate:yyyy-MM-dd} |");
            }

            return result.ToString();
        }, _logger, "GetInsiderOwnership", $"ticker: {ticker}", ReportError);
    }

    [McpServerTool(Name = "SearchInsiders")]
    [Description("Search for corporate insiders (directors, officers, 10% owners) by name. Returns matching insiders with their CIK and role information.")]
    public Task<string> SearchInsiders(
        [Description("Search query for insider name")] string query,
        [Description("Maximum number of results (default: 10)")] int maxResults = 10
    ) {
        return McpToolExecutor.Execute(async () => {
            var insiders = await _ownerRepository.Search(query)
                .Take(maxResults)
                .ToListAsync();

            if (insiders.Count == 0)
                return $"No insiders found matching '{query}'.";

            var result = new StringBuilder();
            result.AppendLine($"Insiders matching '{query}':");
            result.AppendLine();
            result.AppendLine("| Name | CIK | Role | Location |");
            result.AppendLine("|------|-----|------|----------|");

            foreach (var insider in insiders) {
                var role = GetRole(insider);
                var location = string.Join(", ",
                    new[] { insider.City, insider.StateOrCountry }.Where(s => !string.IsNullOrEmpty(s)));
                result.AppendLine($"| {insider.Name} | {insider.OwnerCik} | {role} | {location} |");
            }

            return result.ToString();
        }, _logger, "SearchInsiders", $"query: {query}", ReportError);
    }

    private Task ReportError(string toolName, string message, string stackTrace, string context) {
        return _errorManager.Create(ErrorSource.McpTool, toolName, message, stackTrace, context);
    }

    private static string GetRole(InsiderOwner owner) {
        var roles = new List<string>();
        if (owner.IsDirector) roles.Add("Director");
        if (owner.IsOfficer) roles.Add(owner.OfficerTitle ?? "Officer");
        if (owner.IsTenPercentOwner) roles.Add("10% Owner");
        return roles.Count > 0 ? string.Join(", ", roles) : "Insider";
    }
}
