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

    [McpServerTool(
        Name = "GetInsiderTransactions",
        Title = "Insider Transactions (Forms 3/4/5)",
        ReadOnly = true
    )]
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
                    // This transaction-filter parameter deliberately keeps partial-name
                    // contains semantics; SearchInsiders is the stricter whole-word discovery
                    // surface and returns the filed name callers can pass here.
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
                var transactions = await query.OrderNewestFirst().Take(maxResults).ToListAsync();

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
                var splits = await _stockSplitRepository
                    .GetEffectiveByStock(stock.Id, DateOnly.FromDateTime(DateTime.UtcNow))
                    .ToListAsync();

                var sb = new StringBuilder();
                sb.AppendLine($"Recent insider transactions for {stock.Name} ({stock.Ticker}):");
                sb.AppendLine($"Showing {transactions.Count} most recent transactions");
                sb.AppendLine(
                    "_Shares/Price/Value are as filed; Owned After is the post-transaction balance restated onto today's split basis. Security is the filed security title (kind when the filing names none) — balances are tracked per security and ownership form (see Security/Ownership), not as one running total per insider, so an issuer with several listed securities (e.g. ordinary shares and ADS) shows separate balances. 10b5-1 '-' means the filing predates the 2023 checkbox._"
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
                        // The filed security title distinguishes several securities of the
                        // same kind (e.g. TSM common shares vs ADS); the kind is the
                        // fallback when a filing names no title.
                        var security = MarkdownTable.EscapeCell(
                            t.SecurityTitle,
                            t.SecurityKind.NameForHumans()
                        );
                        return $"| {t.TransactionDate:yyyy-MM-dd} | {t.InsiderOwner.Name} | {role} | {type} | {McpFormat.WholeNumber(t.Shares)} | ${McpFormat.Invariant(t.PricePerShare, "N2")} | ${McpFormat.WholeNumber(value)} | {McpFormat.WholeNumber(ownedAfter)} | {security} | {t.OwnershipNature.NameForHumans()} | {plan} |";
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

    [McpServerTool(
        Name = "GetInsiderOwnership",
        Title = "Insider Ownership Summary",
        ReadOnly = true
    )]
    [Description(
        "Get a summary of insider ownership for a stock, ranked by shares held. Shares Owned is each insider's actual-share (non-derivative) position — options and other derivative holdings are excluded. Each row is as-of the last line of that insider's most recent SEC Form 3/4/5 filing (former insiders may linger with stale dates or zero shares), and share counts are restated onto today's split basis, so they can differ from the raw figures in older filings. Returns at most maxResults insiders (default 30). Use this to understand the insider ownership structure of a company; use GetInsiderTransactions for the underlying trades."
    )]
    public Task<string> GetInsiderOwnership(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description(
            "Maximum number of insiders to return (default: 30, max: 500; values outside 1-500 are clamped)"
        )]
            int maxResults = 30,
        [Description(
            "Number of ranked insiders to skip before returning rows — pass the previous call's shown count to page past the maxResults cap (default: 0)"
        )]
            int offset = 0
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                maxResults = McpLimit.Clamp(maxResults);

                // An ownership summary's Shares Owned must come from the non-derivative
                // (actual shares) table — an options/derivative row is a different balance,
                // and the pre-v6 "no securities owned" sentinels (SecurityKind Unknown) are
                // zero positions by construction.
                var byStock = _transactionRepository
                    .GetByStock(stock)
                    .Where(t => t.SecurityKind == InsiderSecurityKind.NonDerivative);

                // One row per insider — the LAST row of their newest filing, which carries
                // the end-of-day balance (earlier rows of a multi-row Form 4 are intermediate
                // balances). Materialize them ALL before ranking: each row sits on its own
                // split basis, and cutting on the raw counts would under-rank insiders whose
                // last filing predates a large split (pre-split counts are smaller until
                // restated).
                var currentByStock = byStock.OrderCurrentPositionFirst();
                var latestTransactions = await _transactionRepository
                    .GetByStockWithOwner(stock)
                    .Where(t => t.SecurityKind == InsiderSecurityKind.NonDerivative)
                    .Where(t =>
                        t.Id
                        == currentByStock
                            .Where(t2 => t2.InsiderOwnerId == t.InsiderOwnerId)
                            .Select(t2 => t2.Id)
                            .First()
                    )
                    .ToListAsync();

                // Restate every position onto today's basis, then rank and cut on the
                // adjusted holding so the ordering — and the top-N cut itself — compares
                // like with like.
                var splits = await _stockSplitRepository
                    .GetEffectiveByStock(stock.Id, DateOnly.FromDateTime(DateTime.UtcNow))
                    .ToListAsync();
                offset = McpLimit.ClampOffset(offset);
                // Adjusted holdings tie constantly (zero-share former insiders), so the
                // ordering ends on stable keys — an offset over a partial order would
                // silently repeat or skip insiders between pages.
                var ranked = latestTransactions
                    .OrderByDescending(t =>
                        SplitAdjustment.AdjustShareCount(
                            t.SharesOwnedAfter,
                            t.TransactionDate,
                            splits
                        )
                    )
                    .ThenBy(t => t.InsiderOwner.Name)
                    .ThenBy(t => t.Id)
                    .Skip(offset)
                    .Take(maxResults)
                    .ToList();

                if (ranked.Count == 0 && offset > 0)
                    return $"No results at offset {offset} - only {latestTransactions.Count} insiders on file; lower offset.";
                if (ranked.Count == 0)
                    return $"No insider ownership data found for {stock.Ticker}.";

                var sb = new StringBuilder();
                sb.AppendLine($"Insider ownership summary for {stock.Name} ({stock.Ticker}):");
                sb.AppendLine($"Showing {ranked.Count} insiders with most recent data");
                sb.AppendLine(
                    "_Each row is as-of that insider's most recent filing; Shares Owned is the actual-share (non-derivative) end-of-filing balance restated onto today's split basis. Former insiders may linger with stale dates or zero shares._"
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

                var truncation = McpOutput.PagedTruncationNote(
                    ranked.Count,
                    latestTransactions.Count,
                    offset
                );
                if (truncation.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(truncation);
                }

                return sb.ToString();
            },
            "GetInsiderOwnership",
            $"ticker: {ticker}, offset: {offset}"
        );
    }

    [McpServerTool(
        Name = "GetProposedSales",
        Title = "Proposed Insider Sales (Form 144)",
        ReadOnly = true
    )]
    [Description(
        "Get recent proposed insider sales for a stock from SEC Form 144 notices. Each Form 144 is an affiliate's declaration of intent to sell restricted or control securities, showing the seller, their relationship to the company, the number of shares and aggregate market value to be sold, the proposed sale as a share of the issuer's current shares outstanding, the approximate sale date, the broker, and the filer's remarks (including any stated 10b5-1 plan). Results are the most recent notices first and a note flags when more exist than were returned; use fromDate/toDate to scope a period (heavy 10b5-1 filers can flood the recency window with small daily notices). Use this to anticipate upcoming insider selling before it shows up as an executed Form 4."
    )]
    public Task<string> GetProposedSales(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description(
            "Maximum number of notices to return (default: 50, max: 500; values outside 1-500 are clamped)"
        )]
            int maxResults = 50,
        [Description(
            "Optional earliest filing date to include, ISO format yyyy-MM-dd (e.g., 2025-01-01)"
        )]
            string fromDate = null,
        [Description(
            "Optional latest filing date to include, ISO format yyyy-MM-dd (e.g., 2025-12-31)"
        )]
            string toDate = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var query = _form144Repository.GetByStock(stock);

                if (!string.IsNullOrWhiteSpace(fromDate))
                {
                    if (!McpOutput.TryParseDate(fromDate, out var from))
                        return McpOutput.InvalidArgument("fromDate", fromDate, "yyyy-MM-dd");
                    var fromDay = DateOnly.FromDateTime(from);
                    query = query.Where(f => f.FilingDate >= fromDay);
                }

                if (!string.IsNullOrWhiteSpace(toDate))
                {
                    if (!McpOutput.TryParseDate(toDate, out var to))
                        return McpOutput.InvalidArgument("toDate", toDate, "yyyy-MM-dd");
                    var toDay = DateOnly.FromDateTime(to);
                    query = query.Where(f => f.FilingDate <= toDay);
                }

                var totalCount = await query.CountAsync();
                if (totalCount == 0)
                    return $"No Form 144 proposed sales found for {stock.Ticker}.";

                var filings = await query
                    .OrderNewestFirst()
                    .Take(McpLimit.Clamp(maxResults))
                    .ToListAsync();

                // Each Form 144 is an as-filed notice: the proposed Shares pair with the
                // notice's own Aggregate Market Value, so both stay exactly as reported. The
                // list is ordered by filing date, not by an adjusted quantity, so a filed share
                // count is never compared across a split here. % Outstanding divides by the
                // ISSUER record's share count, not the notice's own field — filers sometimes
                // type their sale count into noOfUnitsOutstanding, which rendered absurd
                // "100% of outstanding" figures (#7164, EquiblesCommercial). The filed share
                // count is restated onto today's split basis first so the ratio compares like
                // with like against the current issuer count.
                var splits = await _stockSplitRepository
                    .GetEffectiveByStock(stock.Id, DateOnly.FromDateTime(DateTime.UtcNow))
                    .ToListAsync();
                var result = MarkdownTable.Start(
                    $"Recent proposed sales (Form 144) for {stock.Name} ({stock.Ticker}):",
                    $"Showing {filings.Count} of {totalCount} most recent notices",
                    "| Filed | Seller | Relationship | Shares | Market Value | % Outstanding | Approx. Sale Date | Broker | Remarks |",
                    "|-------|--------|--------------|--------|--------------|---------------|-------------------|--------|---------|"
                );

                result.AppendRows(
                    filings,
                    f =>
                    {
                        var approxSaleDate =
                            f.ApproxSaleDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            ?? "-";
                        // Remarks is where a filer states the sale runs under a 10b5-1 plan, which
                        // is the difference between pre-scheduled and discretionary selling. Keep
                        // the complete filed text — a plan disclosure can occur at the end.
                        var percentOfOutstanding = FormatPercentOfOutstanding(
                            SplitAdjustment.AdjustShareCount(
                                f.SharesToBeSold,
                                f.FilingDate,
                                splits
                            ),
                            stock.SharesOutStanding
                        );
                        return $"| {f.FilingDate:yyyy-MM-dd} | {MarkdownTable.EscapeCell(f.SellerName, "-")} | {MarkdownTable.EscapeCell(f.RelationshipToIssuer, "-")} | {McpFormat.WholeNumber(f.SharesToBeSold)} | ${McpFormat.WholeNumber(f.AggregateMarketValue)} | {percentOfOutstanding} | {approxSaleDate} | {MarkdownTable.EscapeCell(f.BrokerName, "-")} | {MarkdownTable.EscapeCell(f.Remarks, "-")} |";
                    }
                );

                var note = McpOutput.TruncationNote(filings.Count, totalCount);
                if (note.Length > 0)
                {
                    result.AppendLine();
                    result.AppendLine(note);
                }

                return result.ToString();
            },
            "GetProposedSales",
            $"ticker: {ticker}"
        );
    }

    // The standard Form 144 materiality signal: the proposed sale as a share of the ISSUER
    // record's shares outstanding (the notice's own noOfUnitsOutstanding field is not trusted —
    // filers sometimes type their sale count there). "-" when the issuer share count is unknown.
    private static string FormatPercentOfOutstanding(long sharesToBeSold, long sharesOutstanding)
    {
        if (sharesOutstanding <= 0)
            return "-";
        var percent = sharesToBeSold / (decimal)sharesOutstanding * 100m;
        return McpFormat.Invariant(percent, "0.####") + "%";
    }

    [McpServerTool(Name = "SearchInsiders", Title = "Search Corporate Insiders", ReadOnly = true)]
    [Description(
        "Search the tracked SEC corporate-insider set (directors, officers, 10% owners) by name. Search first requires every punctuation-independent whole query word in the filed legal name, then broadens to any whole word only when no strict row matches; a token inside a different word is not a match. Verified public-name aliases such as Jensen Huang resolve to the SEC owner identity. Returns CIK, role, latest filing company, and location, ordered by recent filing activity."
    )]
    public Task<string> SearchInsiders(
        [Description("Search query for insider name")] string query,
        [Description(
            "Maximum number of results (default: 10, max: 500; values outside 1-500 are clamped)"
        )]
            int maxResults = 10,
        [Description(
            "Number of matches to skip before returning rows — pass the previous call's shown count to page past the maxResults cap (default: 0)"
        )]
            int offset = 0
    )
    {
        return _runner.Execute(
            async () =>
            {
                maxResults = McpLimit.Clamp(maxResults);
                offset = McpLimit.ClampOffset(offset);

                var matches = _ownerRepository.Search(query);
                var total = await matches.CountAsync();

                var insiders = await matches
                    .OrderDiscoveryMatches()
                    .Skip(offset)
                    .Take(maxResults)
                    .ToListAsync();

                if (insiders.Count == 0 && offset > 0)
                    return $"No results at offset {offset} - only {total} insiders match; lower offset.";
                if (insiders.Count == 0)
                    return $"No match for '{query}' in the tracked SEC insider set. This result describes only tracked filers; try fewer name words or the filed surname.";

                // The issuer of each owner's most recent transaction — the affiliation that
                // disambiguates common surnames and gives the caller the ticker the sibling
                // tools are keyed on.
                var ownerIds = insiders.Select(i => i.Id).ToList();
                var byOwners = _transactionRepository.GetByOwnerIds(ownerIds);
                var newestByOwners = byOwners.OrderCurrentPositionFirst();
                var latestByOwner = (
                    await byOwners
                        .Where(t =>
                            t.Id
                            == newestByOwners
                                .Where(t2 => t2.InsiderOwnerId == t.InsiderOwnerId)
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

                var truncation = McpOutput.PagedTruncationNote(insiders.Count, total, offset);
                if (truncation.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(truncation);
                }

                return sb.ToString();
            },
            "SearchInsiders",
            $"query: {query}, offset: {offset}"
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
