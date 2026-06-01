using System.ComponentModel;
using System.Globalization;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.InsiderTrading.Mcp.Tools;

[McpServerToolType]
public class InsiderTradingTools
{
    private readonly InsiderTransactionRepository _transactionRepository;
    private readonly InsiderOwnerRepository _ownerRepository;
    private readonly Form144FilingRepository _form144Repository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly McpToolRunner _runner;

    public InsiderTradingTools(
        InsiderTransactionRepository transactionRepository,
        InsiderOwnerRepository ownerRepository,
        Form144FilingRepository form144Repository,
        CommonStockRepository commonStockRepository,
        ErrorManager errorManager,
        ILogger<InsiderTradingTools> logger
    )
    {
        _transactionRepository = transactionRepository;
        _ownerRepository = ownerRepository;
        _form144Repository = form144Repository;
        _commonStockRepository = commonStockRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetInsiderTransactions")]
    [Description(
        "Get recent insider trading transactions (purchases, sales, awards) for a stock from SEC Form 3 and Form 4 filings. Shows insider name, role, transaction type, shares, price, and post-transaction ownership. Use this to understand insider buying/selling activity."
    )]
    public Task<string> GetInsiderTransactions(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Maximum number of transactions to return (default: 50)")] int maxResults = 50
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                // A negative maxResults would flow into .Take(...) as a negative SQL LIMIT,
                // which PostgreSQL rejects and surfaces as the internal-error sentinel. Clamp
                // so a non-positive cap yields zero rows and the existing no-data message.
                maxResults = Math.Max(0, maxResults);

                var transactions = await _transactionRepository
                    .GetByStock(stock)
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
                result.AppendLine(
                    "| Date | Insider | Role | Type | Shares | Price | Value | Owned After |"
                );
                result.AppendLine(
                    "|------|---------|------|------|--------|-------|-------|-------------|"
                );

                foreach (var t in transactions)
                {
                    var role = GetRole(t.InsiderOwner);
                    var type = t.TransactionCode switch
                    {
                        TransactionCode.Award => "Award",
                        TransactionCode.Gift => "Gift",
                        TransactionCode.Exercise => "Exercise",
                        _ => t.AcquiredDisposed == AcquiredDisposed.Acquired ? "Buy" : "Sell",
                    };

                    var value = t.Shares * t.PricePerShare;
                    result.AppendLine(
                        $"| {t.TransactionDate:yyyy-MM-dd} | {t.InsiderOwner.Name} | {role} | {type} | {t.Shares.ToString("N0", CultureInfo.InvariantCulture)} | ${t.PricePerShare.ToString("N2", CultureInfo.InvariantCulture)} | ${value.ToString("N0", CultureInfo.InvariantCulture)} | {t.SharesOwnedAfter.ToString("N0", CultureInfo.InvariantCulture)} |"
                    );
                }

                return result.ToString();
            },
            "GetInsiderTransactions",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "GetInsiderOwnership")]
    [Description(
        "Get a summary of current insider ownership for a stock. Shows each insider, their role, total shares held, and their most recent transaction. Use this to understand the insider ownership structure of a company."
    )]
    public Task<string> GetInsiderOwnership(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var byStock = _transactionRepository.GetByStock(stock);

                var latestTransactions = await byStock
                    .Where(t =>
                        t.Id
                        == byStock
                            .Where(t2 => t2.InsiderOwnerId == t.InsiderOwnerId)
                            .OrderByDescending(t2 => t2.TransactionDate)
                            .ThenByDescending(t2 => t2.FilingDate)
                            .Select(t2 => t2.Id)
                            .First()
                    )
                    .Include(t => t.InsiderOwner)
                    .OrderByDescending(t => t.SharesOwnedAfter)
                    .Take(30)
                    .ToListAsync();

                if (latestTransactions.Count == 0)
                    return $"No insider ownership data found for {ticker}.";

                var result = new StringBuilder();
                result.AppendLine($"Insider ownership summary for {stock.Name} ({ticker}):");
                result.AppendLine(
                    $"Showing {latestTransactions.Count} insiders with most recent data"
                );
                result.AppendLine();
                result.AppendLine(
                    "| Insider | Role | Shares Owned | Last Transaction | Last Date |"
                );
                result.AppendLine("|---------|------|-------------|-----------------|-----------|");

                foreach (var t in latestTransactions)
                {
                    var role = GetRole(t.InsiderOwner);
                    var lastType = t.TransactionCode.ToString();
                    // Format with InvariantCulture so the MCP markdown does not fork the
                    // separators by host locale (e.g. de-DE would render 7.654.321).
                    var sharesOwned = t.SharesOwnedAfter.ToString(
                        "N0",
                        CultureInfo.InvariantCulture
                    );
                    result.AppendLine(
                        $"| {t.InsiderOwner.Name} | {role} | {sharesOwned} | {lastType} | {t.TransactionDate:yyyy-MM-dd} |"
                    );
                }

                return result.ToString();
            },
            "GetInsiderOwnership",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "GetProposedSales")]
    [Description(
        "Get recent proposed insider sales for a stock from SEC Form 144 notices. Each Form 144 is an affiliate's declaration of intent to sell restricted or control securities, showing the seller, their relationship to the company, the number of shares and aggregate market value to be sold, the approximate sale date, and the broker. Use this to anticipate upcoming insider selling before it shows up as an executed Form 4."
    )]
    public Task<string> GetProposedSales(
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

                // A negative maxResults would flow into .Take(...) as a negative SQL LIMIT,
                // which PostgreSQL rejects and surfaces as the internal-error sentinel. Clamp
                // so a non-positive cap yields zero rows and the existing no-results message.
                var filings = await _form144Repository
                    .GetByStock(stock)
                    .OrderByDescending(f => f.FilingDate)
                    .Take(Math.Max(0, maxResults))
                    .ToListAsync();

                if (filings.Count == 0)
                    return $"No Form 144 proposed sales found for {ticker}.";

                var result = new StringBuilder();
                result.AppendLine($"Recent proposed sales (Form 144) for {stock.Name} ({ticker}):");
                result.AppendLine($"Showing {filings.Count} most recent notices");
                result.AppendLine();
                result.AppendLine(
                    "| Filed | Seller | Relationship | Shares | Market Value | Approx. Sale Date | Broker |"
                );
                result.AppendLine(
                    "|-------|--------|--------------|--------|--------------|-------------------|--------|"
                );

                foreach (var f in filings)
                {
                    var approxSaleDate =
                        f.ApproxSaleDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        ?? "-";
                    result.AppendLine(
                        $"| {f.FilingDate:yyyy-MM-dd} | {f.SellerName} | {f.RelationshipToIssuer} | {f.SharesToBeSold.ToString("N0", CultureInfo.InvariantCulture)} | ${f.AggregateMarketValue.ToString("N0", CultureInfo.InvariantCulture)} | {approxSaleDate} | {f.BrokerName} |"
                    );
                }

                return result.ToString();
            },
            "GetProposedSales",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "SearchInsiders")]
    [Description(
        "Search for corporate insiders (directors, officers, 10% owners) by name. Returns matching insiders with their CIK and role information."
    )]
    public Task<string> SearchInsiders(
        [Description("Search query for insider name")] string query,
        [Description("Maximum number of results (default: 10)")] int maxResults = 10
    )
    {
        return _runner.Execute(
            async () =>
            {
                // A negative maxResults would flow into .Take(...) as a negative SQL LIMIT,
                // which PostgreSQL rejects and surfaces as the internal-error sentinel. Clamp
                // so a non-positive cap yields zero rows and the existing no-results message.
                maxResults = Math.Max(0, maxResults);

                var insiders = await _ownerRepository.Search(query).Take(maxResults).ToListAsync();

                if (insiders.Count == 0)
                    return $"No insiders found matching '{query}'.";

                var result = MarkdownTable.Start(
                    $"Insiders matching '{query}':",
                    "| Name | CIK | Role | Location |",
                    "|------|-----|------|----------|"
                );

                foreach (var insider in insiders)
                {
                    var role = GetRole(insider);
                    var location = string.Join(
                        ", ",
                        new[] { insider.City, insider.StateOrCountry }.Where(s =>
                            !string.IsNullOrEmpty(s)
                        )
                    );
                    result.AppendLine(
                        $"| {insider.Name} | {insider.OwnerCik} | {role} | {location} |"
                    );
                }

                return result.ToString();
            },
            "SearchInsiders",
            $"query: {query}"
        );
    }

    private static string GetRole(InsiderOwner owner)
    {
        var roles = new List<string>();
        if (owner.IsDirector)
            roles.Add("Director");
        if (owner.IsOfficer)
            roles.Add(
                string.IsNullOrWhiteSpace(owner.OfficerTitle) ? "Officer" : owner.OfficerTitle
            );
        if (owner.IsTenPercentOwner)
            roles.Add("10% Owner");
        return roles.Count > 0 ? string.Join(", ", roles) : "Insider";
    }

    // Thin forwarder so existing reflection-based normalization tests still find the method.
    private Task<(CommonStock Stock, string Error)> ResolveStockByTicker(string ticker) =>
        _commonStockRepository.ResolveByTicker(ticker);
}
