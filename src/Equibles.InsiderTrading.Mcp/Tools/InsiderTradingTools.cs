using System.ComponentModel;
using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.InsiderTrading.Data.Extensions;
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
    private readonly StockSplitRepository _stockSplitRepository;
    private readonly McpToolRunner _runner;

    public InsiderTradingTools(
        InsiderTransactionRepository transactionRepository,
        InsiderOwnerRepository ownerRepository,
        Form144FilingRepository form144Repository,
        CommonStockRepository commonStockRepository,
        StockSplitRepository stockSplitRepository,
        ErrorManager errorManager,
        ILogger<InsiderTradingTools> logger
    )
    {
        _transactionRepository = transactionRepository;
        _ownerRepository = ownerRepository;
        _form144Repository = form144Repository;
        _commonStockRepository = commonStockRepository;
        _stockSplitRepository = stockSplitRepository;
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

                maxResults = McpLimit.Clamp(maxResults);

                var transactions = await _transactionRepository
                    .GetByStockWithOwner(stock)
                    .ExcludeHoldings()
                    .OrderByDescending(t => t.TransactionDate)
                    .Take(maxResults)
                    .ToListAsync();

                // Each row is an as-filed record: the per-row Shares, Price, and Value stay
                // exactly as reported so Shares × Price = Value holds within the row (the total
                // value is split-invariant, and a filed quantity is only ever read next to its
                // own price/value — never compared across dates). Only the running
                // post-transaction balance (Owned After) is compared across dates and insiders,
                // so it alone is restated onto today's split basis.
                var splits = await _stockSplitRepository.GetByStock(stock.Id).ToListAsync();

                return MarkdownTable.Render(
                    transactions,
                    $"No insider transactions found for {ticker}.",
                    $"Recent insider transactions for {stock.Name} ({ticker}):",
                    $"Showing {transactions.Count} most recent transactions",
                    "| Date | Insider | Role | Type | Shares | Price | Value | Owned After |",
                    "|------|---------|------|------|--------|-------|-------|-------------|",
                    t =>
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
                        var ownedAfter = SplitAdjustment.AdjustShareCount(
                            t.SharesOwnedAfter,
                            t.TransactionDate,
                            splits
                        );
                        return $"| {t.TransactionDate:yyyy-MM-dd} | {t.InsiderOwner.Name} | {role} | {type} | {McpFormat.WholeNumber(t.Shares)} | ${McpFormat.Invariant(t.PricePerShare, "N2")} | ${McpFormat.WholeNumber(value)} | {McpFormat.WholeNumber(ownedAfter)} |";
                    }
                );
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

                var latestTransactions = await _transactionRepository
                    .GetByStockWithOwner(stock)
                    .Where(t =>
                        t.Id
                        == byStock
                            .Where(t2 => t2.InsiderOwnerId == t.InsiderOwnerId)
                            .OrderByDescending(t2 => t2.TransactionDate)
                            .ThenByDescending(t2 => t2.FilingDate)
                            .Select(t2 => t2.Id)
                            .First()
                    )
                    .OrderByDescending(t => t.SharesOwnedAfter)
                    .Take(30)
                    .ToListAsync();

                // Each insider's most recent position is reported as-of its own transaction
                // date, so different rows sit on different split bases; restate them all onto
                // today's basis and re-rank by the adjusted holding so the ordering is correct.
                var splits = await _stockSplitRepository.GetByStock(stock.Id).ToListAsync();
                var ranked = latestTransactions
                    .OrderByDescending(t =>
                        SplitAdjustment.AdjustShareCount(
                            t.SharesOwnedAfter,
                            t.TransactionDate,
                            splits
                        )
                    )
                    .ToList();

                return MarkdownTable.Render(
                    ranked,
                    $"No insider ownership data found for {ticker}.",
                    $"Insider ownership summary for {stock.Name} ({ticker}):",
                    $"Showing {ranked.Count} insiders with most recent data",
                    "| Insider | Role | Shares Owned | Last Transaction | Last Date |",
                    "|---------|------|-------------|-----------------|-----------|",
                    t =>
                    {
                        var role = GetRole(t.InsiderOwner);
                        var lastType = t.TransactionCode.ToString();
                        var sharesOwned = McpFormat.WholeNumber(
                            SplitAdjustment.AdjustShareCount(
                                t.SharesOwnedAfter,
                                t.TransactionDate,
                                splits
                            )
                        );
                        return $"| {t.InsiderOwner.Name} | {role} | {sharesOwned} | {lastType} | {t.TransactionDate:yyyy-MM-dd} |";
                    }
                );
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

                var filings = await _form144Repository
                    .GetByStock(stock)
                    .OrderByDescending(f => f.FilingDate)
                    .Take(McpLimit.Clamp(maxResults))
                    .ToListAsync();

                // Each Form 144 is an as-filed notice: the proposed Shares pair with the
                // notice's own Aggregate Market Value, so both stay exactly as reported. The
                // list is ordered by filing date, not by an adjusted quantity, so a filed share
                // count is never compared across a split here.
                return MarkdownTable.Render(
                    filings,
                    $"No Form 144 proposed sales found for {ticker}.",
                    $"Recent proposed sales (Form 144) for {stock.Name} ({ticker}):",
                    $"Showing {filings.Count} most recent notices",
                    "| Filed | Seller | Relationship | Shares | Market Value | Approx. Sale Date | Broker |",
                    "|-------|--------|--------------|--------|--------------|-------------------|--------|",
                    f =>
                    {
                        var approxSaleDate =
                            f.ApproxSaleDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            ?? "-";
                        return $"| {f.FilingDate:yyyy-MM-dd} | {f.SellerName} | {f.RelationshipToIssuer} | {McpFormat.WholeNumber(f.SharesToBeSold)} | ${McpFormat.WholeNumber(f.AggregateMarketValue)} | {approxSaleDate} | {f.BrokerName} |";
                    }
                );
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
                maxResults = McpLimit.Clamp(maxResults);

                var insiders = await _ownerRepository.Search(query).Take(maxResults).ToListAsync();

                return MarkdownTable.Render(
                    insiders,
                    $"No insiders found matching '{query}'.",
                    $"Insiders matching '{query}':",
                    "| Name | CIK | Role | Location |",
                    "|------|-----|------|----------|",
                    insider =>
                    {
                        var role = GetRole(insider);
                        var location = string.Join(
                            ", ",
                            new[] { insider.City, insider.StateOrCountry }.Where(s =>
                                !string.IsNullOrEmpty(s)
                            )
                        );
                        return $"| {insider.Name} | {insider.OwnerCik} | {role} | {location} |";
                    }
                );
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
