using System.Text.RegularExpressions;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using HtmlAgilityPack;

namespace Equibles.Congress.HostedService.Services;

public static partial class DisclosureParsingHelper
{
    private const string EmptySentinel = "--";

    public static List<DisclosureTransaction> ParseTransactionsFromHtml(
        string html,
        string memberName,
        CongressPosition position,
        DateOnly filingDate,
        ILogger logger
    )
    {
        var transactions = new List<DisclosureTransaction>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var tables = doc.DocumentNode.SelectNodes("//table");
        if (tables == null)
            return transactions;

        foreach (var table in tables)
        {
            var headerTexts = ExtractHeaderTexts(table);
            if (headerTexts == null || !IsTransactionTable(headerTexts))
                continue;

            var cols = MapColumnIndices(headerTexts);
            var rows = table.SelectNodes(".//tbody//tr");
            if (rows == null)
                continue;

            foreach (var row in rows)
            {
                var tx = ParseTransactionRow(row, cols, memberName, position, filingDate, logger);
                if (tx != null)
                    transactions.Add(tx);
            }
        }

        return transactions;
    }

    private static List<string> ExtractHeaderTexts(HtmlNode table)
    {
        var headers = table.SelectNodes(".//thead//th") ?? table.SelectNodes(".//tr[1]//th");
        return headers?.Select(h => h.InnerText.Trim().ToLowerInvariant()).ToList();
    }

    private static bool IsTransactionTable(List<string> headerTexts)
    {
        var hasDate = headerTexts.Any(h => h.Contains("date"));
        var hasAsset = headerTexts.Any(h =>
            h.Contains("asset") || h.Contains("ticker") || h.Contains("description")
        );
        return hasDate && hasAsset;
    }

    private static ColumnIndices MapColumnIndices(List<string> headers)
    {
        var dateCol = FindFirstIndex(
            headers,
            h => h.Contains("transaction") && h.Contains("date"),
            h => h.Contains("notification") && h.Contains("date"),
            h => h.Contains("date")
        );

        var ownerCol = headers.FindIndex(h => h.Contains("owner") || h.Contains("filer"));
        var tickerCol = headers.FindIndex(h => h.Contains("ticker") || h.Contains("symbol"));

        var assetCol = FindFirstIndex(
            headers,
            h => h.Contains("asset") && h.Contains("name"),
            h => h.Contains("asset") && !h.Contains("type"),
            h => h.Contains("description")
        );

        var assetTypeCol = headers.FindIndex(h => h.Contains("asset") && h.Contains("type"));

        var typeCol = FindFirstIndex(
            headers,
            h => h.Contains("transaction") && h.Contains("type"),
            h => h == "type"
        );

        var amountCol = headers.FindIndex(h => h.Contains("amount"));

        return new ColumnIndices(
            dateCol,
            ownerCol,
            tickerCol,
            assetCol,
            assetTypeCol,
            typeCol,
            amountCol
        );
    }

    private static int FindFirstIndex(List<string> headers, params Predicate<string>[] predicates)
    {
        foreach (var predicate in predicates)
        {
            var idx = headers.FindIndex(predicate);
            if (idx != -1)
                return idx;
        }
        return -1;
    }

    private static DisclosureTransaction ParseTransactionRow(
        HtmlNode row,
        ColumnIndices cols,
        string memberName,
        CongressPosition position,
        DateOnly filingDate,
        ILogger logger
    )
    {
        var cells = row.SelectNodes(".//td");
        if (cells == null)
            return null;

        var cellTexts = cells.Select(c => HtmlEntity.DeEntitize(c.InnerText).Trim()).ToList();

        var txDate = CleanSentinel(GetCell(cellTexts, cols.Date)) is { } dateStr
            ? ParseDate(dateStr)
            : null;
        if (txDate == null)
            return null;

        // Senate HTML has ticker in its own column as raw text or <a> tag inner text
        var ticker = CleanSentinel(GetCell(cellTexts, cols.Ticker));
        var assetName = CleanSentinel(GetCell(cellTexts, cols.Asset));
        if (string.IsNullOrEmpty(ticker) && string.IsNullOrEmpty(assetName))
            return null;

        // Fall back to extracting ticker from asset name (parentheses pattern)
        if (string.IsNullOrEmpty(ticker) && !string.IsNullOrEmpty(assetName))
            ticker = ExtractTickerFromAssetName(assetName);

        // Skip non-stock assets when asset_type column exists
        var assetType = CleanSentinel(GetCell(cellTexts, cols.AssetType));
        if (
            !string.IsNullOrEmpty(assetType)
            && !assetType.Contains("Stock", StringComparison.OrdinalIgnoreCase)
        )
            return null;

        var txTypeStr = CleanSentinel(GetCell(cellTexts, cols.Type));
        var txType = ParseTransactionType(txTypeStr);
        if (txType == null)
        {
            logger.LogDebug(
                "Skipping transaction with unrecognized type '{Type}' for {Member}",
                txTypeStr,
                memberName
            );
            return null;
        }

        var owner = CleanSentinel(GetCell(cellTexts, cols.Owner));
        var amount = CleanSentinel(GetCell(cellTexts, cols.Amount));
        var (amountFrom, amountTo) = ParseAmountRange(amount);

        return new DisclosureTransaction
        {
            MemberName = memberName,
            Position = position,
            Ticker = ticker?.ToUpperInvariant(),
            AssetName = Truncate(assetName, 256),
            TransactionDate = txDate.Value,
            FilingDate = filingDate,
            TransactionType = txType.Value,
            OwnerType = Truncate(owner, 64),
            AmountFrom = amountFrom,
            AmountTo = amountTo,
        };
    }

    public static string GetCell(List<string> cells, int index) =>
        index >= 0 && index < cells.Count ? cells[index] : null;

    public static string CleanSentinel(string value) =>
        string.IsNullOrEmpty(value) || value == EmptySentinel ? null : value;

    public static string Truncate(string value, int maxLength) =>
        value?.Length > maxLength ? value[..maxLength] : value;

    public static DateOnly? ParseDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        // Congressional disclosure dates are US MM/dd/yyyy. Parsing with the
        // host culture misreads "03/04/2025" as 3 April under en-GB/de-DE, so
        // pin US culture (and an invariant exact-format fallback).
        var us = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        if (DateOnly.TryParse(text, us, System.Globalization.DateTimeStyles.None, out var d))
            return d;
        if (
            DateOnly.TryParseExact(
                text,
                "MM/dd/yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out d
            )
        )
            return d;
        return null;
    }

    public static string ExtractTickerFromAssetName(string assetName)
    {
        var match = TickerRegex().Match(assetName);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    // Known types intentionally skipped (no enum value): Exchange, Receive
    // Handles both Senate full words ("Purchase", "Sale (Full)") and House abbreviations ("P", "S", "S (partial)")
    public static CongressTransactionType? ParseTransactionType(string type)
    {
        if (string.IsNullOrEmpty(type))
            return null;
        // The House abbreviation may carry a qualifier suffix ("S (partial)"),
        // so accept the bare letter or the letter followed by a space/'(' in
        // addition to the Senate full words.
        var trimmed = type.Trim();
        if (
            trimmed.Contains("Sale", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("Sold", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("S", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("S ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("S(", StringComparison.OrdinalIgnoreCase)
        )
            return CongressTransactionType.Sale;
        if (
            trimmed.Contains("Purchase", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("Buy", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("P", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("P ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("P(", StringComparison.OrdinalIgnoreCase)
        )
            return CongressTransactionType.Purchase;
        return null;
    }

    public static (long from, long to) ParseAmountRange(string amount)
    {
        if (string.IsNullOrWhiteSpace(amount))
            return (0, 0);

        var matches = AmountRegex().Matches(amount);

        if (matches.Count >= 2)
            return (ParseAmount(matches[0]), ParseAmount(matches[1]));

        if (matches.Count == 1)
        {
            var val = ParseAmount(matches[0]);
            // A single amount is an open-ended lower bound when phrased as
            // "Over $X" or the House top bracket "$X +" (>= $X) — both map to
            // (val, val). Otherwise it is an upper bound, e.g. "Under $X".
            var isOpenTopBracket =
                amount.Contains("Over", StringComparison.OrdinalIgnoreCase)
                || amount.TrimEnd().EndsWith('+');
            return isOpenTopBracket ? (val, val) : (0, val);
        }

        return (0, 0);
    }

    private static long ParseAmount(Match match)
    {
        long.TryParse(match.Groups[1].Value.Replace(",", ""), out var val);
        return val;
    }

    public static bool IsValidDisclosureUrl(string url, string expectedBaseUrl)
    {
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(expectedBaseUrl))
            return false;

        // A StartsWith check is an SSRF bypass: an attacker domain that merely
        // prefixes the base ("house.gov.evil.example") would pass. Compare the
        // actual origin — scheme, host and port must match exactly.
        if (
            !Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || !Uri.TryCreate(expectedBaseUrl, UriKind.Absolute, out var baseUri)
        )
            return false;

        return string.Equals(parsed.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(parsed.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
            && parsed.Port == baseUri.Port;
    }

    // Matches tickers in parentheses/brackets: (AAPL), [MSFT], case-insensitive,
    // including a dotted class-share suffix: (BRK.B), [BF.B]
    [GeneratedRegex(@"[\(\[]\s*([A-Za-z]{1,5}(?:\.[A-Za-z]{1,2})?)\s*[\)\]]")]
    public static partial Regex TickerRegex();

    // Matches dollar amounts: $1,001 — requires $ prefix to avoid false positives
    [GeneratedRegex(@"\$([\d,]+)")]
    public static partial Regex AmountRegex();

    // Matches href attribute in anchor tags
    [GeneratedRegex(@"href=[""']([^""']+)[""']")]
    public static partial Regex HrefRegex();

    private record ColumnIndices(
        int Date,
        int Owner,
        int Ticker,
        int Asset,
        int AssetType,
        int Type,
        int Amount
    );
}
