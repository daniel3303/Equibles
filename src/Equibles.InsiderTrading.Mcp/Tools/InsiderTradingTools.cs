using System.ComponentModel;
using System.Globalization;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.Extensions;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
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

    // User-facing labels accepted by the transactionType argument. Buy/Sell alias the
    // Purchase/Sale codes because those are the labels the table renders; Holding is
    // deliberately absent — position snapshots are excluded from transaction lists
    // (see ExcludeHoldings).
    private static readonly Dictionary<string, TransactionCode> TransactionTypeAliases = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Buy"] = TransactionCode.Purchase,
        ["Purchase"] = TransactionCode.Purchase,
        ["Sell"] = TransactionCode.Sale,
        ["Sale"] = TransactionCode.Sale,
        ["Award"] = TransactionCode.Award,
        ["Conversion"] = TransactionCode.Conversion,
        ["Exercise"] = TransactionCode.Exercise,
        ["TaxPayment"] = TransactionCode.TaxPayment,
        ["Tax Payment"] = TransactionCode.TaxPayment,
        ["Expiration"] = TransactionCode.Expiration,
        ["Gift"] = TransactionCode.Gift,
        ["Inheritance"] = TransactionCode.Inheritance,
        ["Discretionary"] = TransactionCode.Discretionary,
        ["Other"] = TransactionCode.Other,
    };

    private const string AcceptedTransactionTypes =
        "Buy, Sell, Award, Conversion, Exercise, TaxPayment, Expiration, Gift, Inheritance, Discretionary, Other";

    [McpServerTool(Name = "GetInsiderTransactions")]
    [Description(
        "Get recent insider trading transactions for a stock from SEC Form 3/4/5 filings, newest first. The Type column carries the SEC transaction code meaning: 'Buy'/'Sell' are open-market purchases/sales only, while Award, Conversion, Exercise, Tax Payment, Expiration, Gift, Inheritance, Discretionary and Other are compensation or derivative mechanics — not conviction trades. The 10b5-1 column marks trades made under a pre-arranged Rule 10b5-1 plan ('-' = filing predates the 2023 checkbox). Per-row Shares/Price/Value are as filed; Owned After is the post-transaction balance restated onto today's split basis, tracked per security kind and ownership form. Supports optional date-range, transaction-type and insider-name filters to reach history beyond the newest rows. Use this to understand insider buying/selling activity."
    )]
    public Task<string> GetInsiderTransactions(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description(
            "Maximum number of transactions to return (default: 50, max: 500; values outside 1-500 are clamped)"
        )]
            int maxResults = 50,
        [Description(
            "Only include transactions on or after this date, format yyyy-MM-dd (optional)"
        )]
            string fromDate = null,
        [Description(
            "Only include transactions on or before this date, format yyyy-MM-dd (optional)"
        )]
            string toDate = null,
        [Description(
            "Only include one transaction type: Buy, Sell, Award, Conversion, Exercise, TaxPayment, Expiration, Gift, Inheritance, Discretionary or Other (optional)"
        )]
            string transactionType = null,
        [Description(
            "Only include transactions by insiders whose SEC-filed name contains every word of this value, case-insensitive (e.g. 'Huang') (optional)"
        )]
            string insiderName = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                maxResults = McpLimit.Clamp(maxResults);

                var query = _transactionRepository
                    .GetByStockWithOwner(stock)
                    .ExcludeHoldings()
                    // Degenerate rows (zero shares AND zero resulting balance — e.g. a Form 3
                    // filed by an insider owning nothing) carry no information and would burn
                    // maxResults slots.
                    .Where(t => t.Shares != 0 || t.SharesOwnedAfter != 0);

                var filtered = false;
                if (!string.IsNullOrWhiteSpace(fromDate))
                {
                    if (!McpOutput.TryParseDate(fromDate, out var from))
                        return McpOutput.InvalidArgument("fromDate", fromDate, "yyyy-MM-dd");
                    var fromDay = DateOnly.FromDateTime(from);
                    query = query.Where(t => t.TransactionDate >= fromDay);
                    filtered = true;
                }

                if (!string.IsNullOrWhiteSpace(toDate))
                {
                    if (!McpOutput.TryParseDate(toDate, out var to))
                        return McpOutput.InvalidArgument("toDate", toDate, "yyyy-MM-dd");
                    var toDay = DateOnly.FromDateTime(to);
                    query = query.Where(t => t.TransactionDate <= toDay);
                    filtered = true;
                }

                if (!string.IsNullOrWhiteSpace(transactionType))
                {
                    if (!TransactionTypeAliases.TryGetValue(transactionType.Trim(), out var code))
                        return McpOutput.InvalidArgument(
                            "transactionType",
                            transactionType,
                            AcceptedTransactionTypes
                        );
                    query = query.Where(t => t.TransactionCode == code);
                    filtered = true;
                }

                if (!string.IsNullOrWhiteSpace(insiderName))
                {
                    // Same token-AND contains contract as SearchInsiders, against the
                    // SEC-filed legal name.
                    foreach (
                        var token in insiderName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    )
                    {
                        var pattern = LikePattern.Contains(token);
                        query = query.Where(t =>
                            EF.Functions.ILike(t.InsiderOwner.Name, pattern, LikePattern.EscapeChar)
                        );
                    }
                    filtered = true;
                }

                var total = await query.CountAsync();
                var transactions = await query
                    .OrderByDescending(t => t.TransactionDate)
                    .Take(maxResults)
                    .ToListAsync();

                if (transactions.Count == 0)
                    return filtered
                        ? $"No insider transactions found for {stock.Ticker} matching the given filters."
                        : $"No insider transactions found for {stock.Ticker}.";

                // Each row is an as-filed record: the per-row Shares, Price, and Value stay
                // exactly as reported so Shares × Price = Value holds within the row (the total
                // value is split-invariant, and a filed quantity is only ever read next to its
                // own price/value — never compared across dates). Only the running
                // post-transaction balance (Owned After) is compared across dates and insiders,
                // so it alone is restated onto today's split basis.
                var splits = await _stockSplitRepository.GetByStock(stock.Id).ToListAsync();

                var sb = new StringBuilder();
                sb.AppendLine($"Recent insider transactions for {stock.Name} ({stock.Ticker}):");
                sb.AppendLine($"Showing {transactions.Count} most recent transactions");
                sb.AppendLine(
                    "_Shares/Price/Value are as filed; Owned After is the post-transaction balance restated onto today's split basis. Balances are tracked per security kind and ownership form (see Security/Ownership), not as one running total per insider. 10b5-1 '-' means the filing predates the 2023 checkbox._"
                );
                sb.AppendLine();
                sb.AppendLine(
                    "| Date | Insider | Role | Type | Shares | Price | Value | Owned After | Security | Ownership | 10b5-1 |"
                );
                sb.AppendLine(
                    "|------|---------|------|------|--------|-------|-------|-------------|----------|-----------|--------|"
                );
                sb.AppendRows(
                    transactions,
                    t =>
                    {
                        var role = GetRole(t.InsiderOwner);
                        // Reserve the Buy/Sell trade labels strictly for open-market
                        // purchases/sales; every other SEC code renders its own meaning so
                        // comp mechanics (conversions, tax withholding, expirations) are
                        // never mistaken for conviction trades.
                        var type = t.TransactionCode switch
                        {
                            TransactionCode.Purchase => "Buy",
                            TransactionCode.Sale => "Sell",
                            _ => t.TransactionCode.NameForHumans(),
                        };

                        var value = t.Shares * t.PricePerShare;
                        var ownedAfter = SplitAdjustment.AdjustShareCount(
                            t.SharesOwnedAfter,
                            t.TransactionDate,
                            splits
                        );
                        var plan = t.IsRule10b5One switch
                        {
                            true => "Yes",
                            false => "No",
                            null => "-",
                        };
                        return $"| {t.TransactionDate:yyyy-MM-dd} | {t.InsiderOwner.Name} | {role} | {type} | {McpFormat.WholeNumber(t.Shares)} | ${McpFormat.Invariant(t.PricePerShare, "N2")} | ${McpFormat.WholeNumber(value)} | {McpFormat.WholeNumber(ownedAfter)} | {t.SecurityKind.NameForHumans()} | {t.OwnershipNature.NameForHumans()} | {plan} |";
                    }
                );

                var truncation = McpOutput.TruncationNote(transactions.Count, total);
                if (truncation.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(truncation);
                }

                return sb.ToString();
            },
            "GetInsiderTransactions",
            $"ticker: {ticker}, fromDate: {fromDate}, toDate: {toDate}, transactionType: {transactionType}, insiderName: {insiderName}"
        );
    }

    [McpServerTool(Name = "GetInsiderOwnership")]
    [Description(
        "Get a summary of insider ownership for a stock, ranked by shares held. Each row is as-of that insider's most recent SEC Form 3/4/5 filing (former insiders may linger with stale dates or zero shares), and share counts are restated onto today's split basis, so they can differ from the raw figures in older filings. Returns at most maxResults insiders (default 30). Use this to understand the insider ownership structure of a company; use GetInsiderTransactions for the underlying trades."
    )]
    public Task<string> GetInsiderOwnership(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description(
            "Maximum number of insiders to return (default: 30, max: 500; values outside 1-500 are clamped)"
        )]
            int maxResults = 30
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                maxResults = McpLimit.Clamp(maxResults);

                var byStock = _transactionRepository.GetByStock(stock);

                // One row per insider (their latest filing). Materialize them ALL before
                // ranking: each row sits on its own split basis, and cutting on the raw
                // counts would under-rank insiders whose last filing predates a large split
                // (pre-split counts are smaller until restated).
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
                    .ToListAsync();

                // Restate every position onto today's basis, then rank and cut on the
                // adjusted holding so the ordering — and the top-N cut itself — compares
                // like with like.
                var splits = await _stockSplitRepository.GetByStock(stock.Id).ToListAsync();
                var ranked = latestTransactions
                    .OrderByDescending(t =>
                        SplitAdjustment.AdjustShareCount(
                            t.SharesOwnedAfter,
                            t.TransactionDate,
                            splits
                        )
                    )
                    .Take(maxResults)
                    .ToList();

                if (ranked.Count == 0)
                    return $"No insider ownership data found for {stock.Ticker}.";

                var sb = new StringBuilder();
                sb.AppendLine($"Insider ownership summary for {stock.Name} ({stock.Ticker}):");
                sb.AppendLine($"Showing {ranked.Count} insiders with most recent data");
                sb.AppendLine(
                    "_Each row is as-of that insider's most recent filing; Shares Owned is restated onto today's split basis. Former insiders may linger with stale dates or zero shares._"
                );
                sb.AppendLine();
                sb.AppendLine("| Insider | Role | Shares Owned | Last Transaction | Last Date |");
                sb.AppendLine("|---------|------|-------------|-----------------|-----------|");
                sb.AppendRows(
                    ranked,
                    t =>
                    {
                        var role = GetRole(t.InsiderOwner);
                        var lastType = t.TransactionCode.NameForHumans();
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

                var truncation = McpOutput.TruncationNote(ranked.Count, latestTransactions.Count);
                if (truncation.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(truncation);
                }

                return sb.ToString();
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
        [Description(
            "Maximum number of notices to return (default: 50, max: 500; values outside 1-500 are clamped)"
        )]
            int maxResults = 50
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
                    $"No Form 144 proposed sales found for {stock.Ticker}.",
                    $"Recent proposed sales (Form 144) for {stock.Name} ({stock.Ticker}):",
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
        "Search for corporate insiders (directors, officers, 10% owners) by name. Names are matched as filed with the SEC — legal names, frequently 'LAST FIRST MIDDLE' (e.g. Jensen Huang is filed as 'HUANG JEN HSUN') — and every word of the query must appear in the name, so retry with the surname alone when a full name misses. Returns matching insiders with their CIK, role, the company of their most recent filing, and location, ordered by most recent filing activity. Pivot to the sibling ticker-keyed tools with the returned company ticker (e.g. GetInsiderTransactions with its insiderName filter) to see a person's trades."
    )]
    public Task<string> SearchInsiders(
        [Description("Search query for insider name")] string query,
        [Description(
            "Maximum number of results (default: 10, max: 500; values outside 1-500 are clamped)"
        )]
            int maxResults = 10
    )
    {
        return _runner.Execute(
            async () =>
            {
                maxResults = McpLimit.Clamp(maxResults);

                var matches = _ownerRepository.Search(query);
                var total = await matches.CountAsync();

                // Deterministic order: most recently active filers first (the insider the
                // caller wants is far more likely among them than in an arbitrary
                // Postgres-chosen subset of, say, 557 Smiths), then name/CIK as stable
                // tie-breakers. The coalesce keeps never-filed owners last — Postgres
                // sorts NULLs first on a DESC order.
                var insiders = await matches
                    .OrderByDescending(o =>
                        o.Transactions.Max(t => (DateOnly?)t.TransactionDate) ?? DateOnly.MinValue
                    )
                    .ThenBy(o => o.Name)
                    .ThenBy(o => o.OwnerCik)
                    .Take(maxResults)
                    .ToListAsync();

                if (insiders.Count == 0)
                    return $"No insiders found matching '{query}'. Names are matched as filed with the SEC (legal names, often 'LAST FIRST MIDDLE') and every word of the query must appear in the name - retry with the surname alone.";

                // The issuer of each owner's most recent transaction — the affiliation that
                // disambiguates common surnames and gives the caller the ticker the sibling
                // tools are keyed on.
                var ownerIds = insiders.Select(i => i.Id).ToList();
                var byOwners = _transactionRepository.GetByOwnerIds(ownerIds);
                var latestByOwner = (
                    await byOwners
                        .Where(t =>
                            t.Id
                            == byOwners
                                .Where(t2 => t2.InsiderOwnerId == t.InsiderOwnerId)
                                .OrderByDescending(t2 => t2.TransactionDate)
                                .ThenByDescending(t2 => t2.FilingDate)
                                .Select(t2 => t2.Id)
                                .First()
                        )
                        .Include(t => t.CommonStock)
                        .ToListAsync()
                ).ToDictionary(t => t.InsiderOwnerId);

                var sb = new StringBuilder();
                sb.AppendLine($"Insiders matching '{query}':");
                sb.AppendLine();
                sb.AppendLine("| Name | CIK | Role | Company (latest filing) | Location |");
                sb.AppendLine("|------|-----|------|-------------------------|----------|");
                sb.AppendRows(
                    insiders,
                    insider =>
                    {
                        var role = GetRole(insider);
                        var company =
                            latestByOwner.TryGetValue(insider.Id, out var transaction)
                            && transaction.CommonStock != null
                                ? $"{transaction.CommonStock.Name} ({transaction.CommonStock.Ticker})"
                                : "-";
                        var location = string.Join(
                            ", ",
                            new[] { insider.City, insider.StateOrCountry }.Where(s =>
                                !string.IsNullOrEmpty(s)
                            )
                        );
                        return $"| {insider.Name} | {insider.OwnerCik} | {role} | {company} | {location} |";
                    }
                );

                var truncation = McpOutput.TruncationNote(insiders.Count, total);
                if (truncation.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(truncation);
                }

                return sb.ToString();
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
