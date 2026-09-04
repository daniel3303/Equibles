using System.ComponentModel;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Congress.Data;
using Equibles.Congress.Data.Extensions;
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

    [McpServerTool(
        Name = "GetCongressionalTrades",
        Title = "Congressional Trades by Stock",
        ReadOnly = true
    )]
    [Description(
        "Get congressional securities transactions for a specific ticker (newest first, last year by default). Shows which members of Congress reported a purchase or sale, with transaction and filing dates; amounts are disclosed ranges, not exact values, and Asset identifies the filed instrument (such as stock, option, or bond). Use GetMemberTrades for one member's transactions across all tickers."
    )]
    public Task<string> GetCongressionalTrades(
        [Description("Listed security ticker (e.g., AAPL, VOO, MSFT)")] string ticker,
        [Description(
            "Filter by transaction type: Purchase or Sale; the synonyms Buy/Sell are accepted (defaults to all)"
        )]
            string transactionType = null,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to today)")] string endDate = null,
        [Description("Maximum number of trades to return (default: 50, max: 500, newest first)")]
            int maxResults = 50,
        [Description("Number of matching trades to skip before returning rows (default: 0)")]
            int offset = 0
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                var (start, end, rangeError) = ParseTradeDateRange(startDate, endDate);
                if (rangeError != null)
                    return rangeError;

                var (typeFilter, typeError) = ParseTransactionTypeArgument(transactionType);
                if (typeError != null)
                    return typeError;

                var listedTicker = SecondaryTickerPolicy.ResolveListedTicker(stock, ticker);
                var query = _tradeRepository.GetByListing(stock, listedTicker, start, end);
                if (typeFilter != null)
                    query = query.Where(t => t.TransactionType == typeFilter);

                maxResults = McpLimit.Clamp(maxResults);
                offset = McpLimit.ClampOffset(offset);
                var totalCount = await query.CountAsync();

                var trades = await query
                    .Include(t => t.CongressMember)
                    .OrderNewestFirst()
                    .Skip(offset)
                    .Take(maxResults)
                    .ToListAsync();
                if (trades.Count == 0 && offset > 0)
                    return $"No results at offset {offset} - only {totalCount} trades match; lower offset.";

                var table = MarkdownTable.Render(
                    trades,
                    $"No congressional trades found for {listedTicker} between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}.",
                    $"Congressional trades for {ListingLabel(stock, listedTicker)}, {start:yyyy-MM-dd} to {end:yyyy-MM-dd}:",
                    "| Date | Filed | Member | Position | Type | Amount Range | Asset | Asset Type | Owner | Account |",
                    "|------|-------|--------|----------|------|-------------|-------|------------|-------|---------|",
                    t =>
                    {
                        var position = t.CongressMember.Position.NameForHumans();
                        var type = t.TransactionType.NameForHumans();
                        var amount = FormatAmountRange(t);
                        // The asset as filed is the ONLY thing separating an option or bond trade
                        // from a stock trade — "Purchase" alone reads as a bullish share buy even
                        // when the filing says it was a put.
                        return $"| {t.TransactionDate:yyyy-MM-dd} | {t.FilingDate:yyyy-MM-dd} | {t.CongressMember.Name} | {position} | {type} | {amount} | {EscapeCell(t.AssetName)} | {EscapeCell(t.AssetType)} | {FormatOwner(t.OwnerType)} | {EscapeCell(t.Subholding)} |";
                    }
                );
                var note = McpOutput.PagedTruncationNote(trades.Count, totalCount, offset);
                return note.Length == 0 ? table : $"{table}\n{note}";
            },
            "GetCongressionalTrades",
            $"ticker: {ticker}, offset: {offset}"
        );
    }

    [McpServerTool(Name = "GetMemberTrades", Title = "Trades by Congress Member", ReadOnly = true)]
    [Description(
        "Get a congress member's disclosed securities transactions (newest first, last year by default). Shows tickers, transaction and filing dates, disclosed amount ranges, and the filed Asset identifying the instrument (such as stock, option, or bond). Use SearchCongressMembers to find member names, and GetCongressionalTrades for all members' transactions in one ticker."
    )]
    public Task<string> GetMemberTrades(
        [Description(
            "Congress member name, case-insensitive (e.g., 'Nancy Pelosi', 'Dan Crenshaw'); use SearchCongressMembers to find the exact name"
        )]
            string memberName,
        [Description(
            "Filter by transaction type: Purchase or Sale; the synonyms Buy/Sell are accepted (defaults to all)"
        )]
            string transactionType = null,
        [Description("Start date in YYYY-MM-DD format (defaults to 1 year ago)")]
            string startDate = null,
        [Description("End date in YYYY-MM-DD format (defaults to today)")] string endDate = null,
        [Description("Maximum number of trades to return (default: 50, max: 500, newest first)")]
            int maxResults = 50,
        [Description(
            "Number of trades to skip before returning rows — pass the previous call's shown count to page past the maxResults cap (default: 0)"
        )]
            int offset = 0,
        [Description("Optional stock ticker to combine with the member filter (e.g., AAPL)")]
            string ticker = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(memberName))
                    return "Provide a congress member name. Use SearchCongressMembers to inspect the tracked roster.";

                var member = await ResolveMember(memberName.Trim());
                if (member == null)
                    return $"Member '{memberName}' not found in the tracked congressional roster. Use SearchCongressMembers to inspect the tracked roster and find the exact filed name.";

                var (start, end, rangeError) = ParseTradeDateRange(startDate, endDate);
                if (rangeError != null)
                    return rangeError;

                var (typeFilter, typeError) = ParseTransactionTypeArgument(transactionType);
                if (typeError != null)
                    return typeError;

                CommonStock stock = null;
                string listedTicker = null;
                if (!string.IsNullOrWhiteSpace(ticker))
                {
                    var (resolvedStock, stockError) = await _commonStockRepository.ResolveByTicker(
                        ticker
                    );
                    if (stockError != null)
                        return stockError;
                    stock = resolvedStock;
                    listedTicker = SecondaryTickerPolicy.ResolveListedTicker(stock, ticker);
                }

                var query = _tradeRepository
                    .GetByMember(member)
                    .Where(t => t.TransactionDate >= start && t.TransactionDate <= end);
                if (typeFilter != null)
                    query = query.Where(t => t.TransactionType == typeFilter);
                if (stock != null)
                    query = query.Where(t =>
                        t.CommonStockId == stock.Id
                        && (
                            t.FiledTicker == listedTicker
                            || (t.FiledTicker == "" && listedTicker == stock.Ticker)
                        )
                    );

                maxResults = McpLimit.Clamp(maxResults);
                offset = McpLimit.ClampOffset(offset);
                var totalCount = await query.CountAsync();

                var trades = await query
                    .Include(t => t.CommonStock)
                    .OrderNewestFirst()
                    .Skip(offset)
                    .Take(maxResults)
                    .ToListAsync();
                if (trades.Count == 0 && offset > 0)
                    return $"No results at offset {offset} - only {totalCount} trades match; lower offset.";

                var table = MarkdownTable.Render(
                    trades,
                    $"No trades found for {member.Name} ({DescribeMember(member)}){(stock == null ? "" : $" in {listedTicker}")} between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}.",
                    $"Trades by {member.Name} ({DescribeMember(member)}){(stock == null ? "" : $" in {listedTicker}")}, {start:yyyy-MM-dd} to {end:yyyy-MM-dd}:",
                    "| Date | Filed | Ticker | Type | Amount Range | Asset | Asset Type | Owner | Account |",
                    "|------|-------|--------|------|-------------|-------|------------|-------|---------|",
                    t =>
                    {
                        var type = t.TransactionType.NameForHumans();
                        var amount = FormatAmountRange(t);
                        var filedTicker = string.IsNullOrWhiteSpace(t.FiledTicker)
                            ? t.CommonStock?.Ticker
                            : t.FiledTicker;
                        return $"| {t.TransactionDate:yyyy-MM-dd} | {t.FilingDate:yyyy-MM-dd} | {filedTicker} | {type} | {amount} | {EscapeCell(t.AssetName)} | {EscapeCell(t.AssetType)} | {FormatOwner(t.OwnerType)} | {EscapeCell(t.Subholding)} |";
                    }
                );
                var note = McpOutput.PagedTruncationNote(trades.Count, totalCount, offset);
                return note.Length == 0 ? table : $"{table}\n{note}";
            },
            "GetMemberTrades",
            $"memberName: {memberName}, ticker: {ticker}, offset: {offset}"
        );
    }

    private static string ListingLabel(CommonStock stock, string listedTicker) =>
        SecondaryTickerPolicy.RequiresExactListingScope(stock, listedTicker)
            ? listedTicker
            : $"{listedTicker} ({stock.Name})";

    [McpServerTool(
        Name = "GetMemberNetWorth",
        Title = "Congress Member Net Worth",
        ReadOnly = true
    )]
    [Description(
        "Get a congress member's net worth history from their annual financial disclosures. Disclosed values are ranges, so every year is a band (minimum-maximum), never a point estimate. Only electronically filed reports are read: a missing year means no electronic filing, not zero net worth. Use SearchCongressMembers to find member names."
    )]
    public Task<string> GetMemberNetWorth(
        [Description(
            "Congress member name, case-insensitive (e.g., 'Nancy Pelosi', 'Marsha Blackburn'); use SearchCongressMembers to find the exact name"
        )]
            string memberName,
        [Description("Maximum number of years to return (default: 20, max: 500, newest first)")]
            int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                var member = await ResolveMember(memberName.Trim());
                if (member == null)
                    return $"Member '{memberName}' not found in the tracked congressional roster. Use SearchCongressMembers to inspect the tracked roster and find the exact filed name.";

                var query = _disclosureRepository.GetByMember(member);
                var totalCount = await query.CountAsync();

                var disclosures = await query
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

                var table = MarkdownTable.Render(
                    disclosures,
                    $"No electronically filed annual disclosure found for {member.Name} ({DescribeMember(member)}). Paper filings are not read, so this does not mean zero net worth.",
                    $"Net worth of {member.Name} ({DescribeMember(member)}), as disclosed in annual financial reports (band = sum of disclosed asset minimums minus liability maximums, through asset maximums minus liability minimums; Asset Lines / Liability Lines are counts of disclosed line items, not dollar amounts):",
                    "| Year | Filed | Net Worth Minimum | Net Worth Maximum | Asset Lines | Liability Lines |",
                    "|------|-------|-------------------|-------------------|-------------|-----------------|",
                    d =>
                        $"| {d.Year} | {d.FiledDate:yyyy-MM-dd} | {FormatSignedAmount(d.NetWorthMinimum)} | {FormatSignedAmount(d.NetWorthMaximum)} | {d.AssetCount} | {d.LiabilityCount} |"
                );
                return AppendTruncationNote(table, disclosures.Count, totalCount);
            },
            "GetMemberNetWorth",
            $"memberName: {memberName}"
        );
    }

    // How a member is identified in prose: the chamber, plus the seat when one
    // is recorded. Only the House index publishes a seat, so senators read as
    // "Senator" alone rather than carrying an empty bracket.
    private static string DescribeMember(CongressMember member)
    {
        var seat = CongressSeat.Format(member.StateDistrict);
        var position = member.Position.NameForHumans();
        return seat == null ? position : $"{position}, {seat}";
    }

    // Net worth bands can be negative (liabilities exceeding assets).
    private static string FormatSignedAmount(long value) =>
        value < 0 ? $"-${McpFormat.WholeNumber(-value)}" : $"${McpFormat.WholeNumber(value)}";

    [McpServerTool(
        Name = "SearchCongressMembers",
        Title = "Search Congress Members",
        ReadOnly = true
    )]
    [Description(
        "Search the tracked congressional roster by name. Search first requires every punctuation-independent query word anywhere in the filed name, then broadens to any word only when no strict row matches. Verified public-name aliases such as Dan Crenshaw resolve to the roster name. Returns each match with its position; pass the returned exact Name to GetMemberTrades or GetMemberNetWorth."
    )]
    public Task<string> SearchCongressMembers(
        [Description("Search query — partial or full name (e.g., 'Pelosi', 'Cruz', 'Dan')")]
            string query,
        [Description("Filter by position: Senator or Representative (defaults to both)")]
            string position = null,
        [Description("Maximum number of results to return (default: 20, max: 500)")]
            int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(query))
                    return "Provide part or all of a congress member's name.";

                var (positionFilter, positionError) = ParsePositionArgument(position);
                if (positionError != null)
                    return positionError;

                maxResults = McpLimit.Clamp(maxResults);

                var memberQuery = _memberRepository.Search(query.Trim());
                if (positionFilter != null)
                    memberQuery = memberQuery.Where(m => m.Position == positionFilter);

                var totalCount = await memberQuery.CountAsync();

                var members = await memberQuery.OrderBy(m => m.Name).Take(maxResults).ToListAsync();

                var table = MarkdownTable.Render(
                    members,
                    $"No match for '{query}' in the tracked congressional roster. This result describes only the tracked roster.",
                    $"Congress members matching '{query}':",
                    "| Name | Position | Seat |",
                    "|------|----------|------|",
                    m =>
                        $"| {MarkdownTable.EscapeCell(m.Name, "-")} "
                        + $"| {MarkdownTable.EscapeCell(m.Position.NameForHumans(), "-")} "
                        + $"| {MarkdownTable.EscapeCell(CongressSeat.Format(m.StateDistrict), "-")} |"
                );
                return AppendTruncationNote(table, members.Count, totalCount);
            },
            "SearchCongressMembers",
            $"query: {query}, position: {position}"
        );
    }

    // Exact lookup is case-insensitive (see CongressMemberRepository.GetByName); when it
    // misses, fall back to a contains-match only when it is unambiguous — 'pelosi' resolves,
    // while a query matching several members still asks the caller to disambiguate via
    // SearchCongressMembers.
    private async Task<CongressMember> ResolveMember(string memberName)
    {
        var member = await _memberRepository.GetByName(memberName);
        if (member != null)
            return member;

        var matches = await _memberRepository.Search(memberName).Take(2).ToListAsync();
        return matches.Count == 1 ? matches[0] : null;
    }

    // Format with InvariantCulture so the MCP markdown does not fork the separators by host
    // locale (e.g. de-DE would render $1.000.000).
    private static string FormatAmountRange(CongressionalTrade t) =>
        $"${McpFormat.WholeNumber(t.AmountFrom)}–${McpFormat.WholeNumber(t.AmountTo)}";

    // House PTRs disclose raw owner codes (SP/JT/DC) while Senate rows arrive spelled out;
    // render one vocabulary — the Senate labels — so a table never mixes "SP" and "Spouse".
    // The stored value is untouched: OwnerType is part of the trade upsert key.
    // The asset as filed is free text and is the field that separates an option or bond trade from
    // a share trade, so an unescaped pipe would shift every later column and file Owner under
    // Asset — a classification error, not just a cosmetic one.
    private static string EscapeCell(string value) => MarkdownTable.EscapeCell(value, "—");

    private static string FormatOwner(string ownerType) =>
        string.IsNullOrEmpty(ownerType)
            ? "—"
            : ownerType.ToUpperInvariant() switch
            {
                "SP" => "Spouse",
                "JT" => "Joint",
                "DC" => "Child",
                _ => ownerType,
            };

    // TruncationNote is empty when nothing was cut off; the blank line keeps the note out of
    // the markdown table block.
    private static string AppendTruncationNote(string table, int shown, int total)
    {
        var note = McpOutput.TruncationNote(shown, total);
        return note.Length == 0 ? table : $"{table}\n{note}";
    }

    // Strict date arguments: a malformed date must correct the caller, never silently fall
    // back to the default window — the caller could not tell which range was actually applied.
    private static (DateOnly Date, string Error) ParseDateArgument(
        string value,
        string argumentName,
        DateOnly fallback
    )
    {
        if (string.IsNullOrWhiteSpace(value))
            return (fallback, null);
        if (McpOutput.TryParseDate(value, out var parsed))
            return (DateOnly.FromDateTime(parsed), null);
        return (default, McpOutput.InvalidArgument(argumentName, value, "yyyy-MM-dd"));
    }

    private static (DateOnly Start, DateOnly End, string Error) ParseTradeDateRange(
        string startDate,
        string endDate
    )
    {
        var (start, startError) = ParseDateArgument(
            startDate,
            "startDate",
            McpToolExecutor.UtcYearsAgo(1)
        );
        if (startError != null)
            return (default, default, startError);

        var (end, endError) = ParseDateArgument(
            endDate,
            "endDate",
            DateOnly.FromDateTime(DateTime.UtcNow)
        );
        if (endError != null)
            return (default, default, endError);

        if (start > end)
            return (
                default,
                default,
                $"Invalid date range: startDate {start:yyyy-MM-dd} is after endDate {end:yyyy-MM-dd}."
            );

        return (start, end, null);
    }

    // Purchase/Sale plus the synonyms an LLM naturally reaches for. Deliberately NOT
    // Enum.TryParse: that accepts numeric strings ("2"), producing an undefined enum value
    // that silently filters every row out.
    private static readonly Dictionary<string, CongressTransactionType> TransactionTypeAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Purchase"] = CongressTransactionType.Purchase,
            ["Buy"] = CongressTransactionType.Purchase,
            ["Sale"] = CongressTransactionType.Sale,
            ["Sell"] = CongressTransactionType.Sale,
        };

    // An unrecognised transaction type must error, never silently return unfiltered results:
    // a caller asking for "Buys" would confidently misread the mixed rows as purchases.
    private static (CongressTransactionType? Type, string Error) ParseTransactionTypeArgument(
        string transactionType
    )
    {
        if (string.IsNullOrWhiteSpace(transactionType))
            return (null, null);
        if (TransactionTypeAliases.TryGetValue(transactionType.Trim(), out var parsed))
            return (parsed, null);
        return (
            null,
            McpOutput.InvalidArgument(
                "transactionType",
                transactionType,
                "Purchase or Sale (synonyms: Buy, Sell)"
            )
        );
    }

    private static readonly Dictionary<string, CongressPosition> PositionAliases = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Senator"] = CongressPosition.Senator,
        ["Senate"] = CongressPosition.Senator,
        ["Representative"] = CongressPosition.Representative,
        ["House"] = CongressPosition.Representative,
    };

    private static (CongressPosition? Position, string Error) ParsePositionArgument(string position)
    {
        if (string.IsNullOrWhiteSpace(position))
            return (null, null);
        if (PositionAliases.TryGetValue(position.Trim(), out var parsed))
            return (parsed, null);
        return (null, McpOutput.InvalidArgument("position", position, "Senator or Representative"));
    }
}
