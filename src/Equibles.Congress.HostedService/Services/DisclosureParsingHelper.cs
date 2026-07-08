using System.Text.RegularExpressions;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Core.Extensions;
using HtmlAgilityPack;

namespace Equibles.Congress.HostedService.Services;

public static partial class DisclosureParsingHelper
{
    private const string EmptySentinel = "--";

    // Honorific tokens some filings inject into the disclosed name, with or
    // without a trailing period ("Mr"/"Mr."/"Dr"). They are not part of the
    // name; left in, they fragment one person into several CongressMember
    // records keyed on the raw name string (e.g. "Matt Mr Rosendale" vs
    // "Matt Rosendale" — GH-3374).
    private static readonly HashSet<string> HonorificTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mr",
        "Mrs",
        "Ms",
        "Dr",
        "Hon",
    };

    /// <summary>
    /// Canonicalises a disclosed congressional member name so cosmetic variants
    /// resolve to one identity. The single normaliser every source (House
    /// XML/PDF, Senate) and the member upsert key run through, so a name is
    /// keyed identically no matter which scraper emitted it (GH-3374).
    ///
    /// Applies only the safe, unambiguous transforms:
    /// <list type="bullet">
    /// <item>drops honorific tokens in any position, period-agnostic
    /// ("Scott Mr Franklin" / "Mark Dr Green");</item>
    /// <item>collapses an immediately repeated token — the parser's doubled
    /// first name ("Scott Scott Franklin");</item>
    /// <item>collapses runs of whitespace and trims.</item>
    /// </list>
    /// Whole-token matching keeps real names like "Mraz" intact. It deliberately
    /// does NOT reconcile initial/order variants ("C. Scott Franklin" vs
    /// "Scott Franklin") — that needs a stable filer id and risks merging two
    /// distinct people.
    /// </summary>
    public static string NormalizeMemberName(string name)
    {
        if (name == null)
            return null;

        var tokens = name.Split(
            (char[])null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        var result = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            if (HonorificTokens.Contains(token.TrimEnd('.')))
                continue;
            // Collapse an immediately repeated token (the parser's doubled first
            // name, "Scott Scott"), but never a repeated single-letter initial:
            // "C. C. Franklin" is a genuine two-initial name and folding it would
            // merge a distinct identity (GH-3989).
            if (
                result.Count > 0
                && !IsInitialToken(token)
                && string.Equals(result[^1], token, StringComparison.OrdinalIgnoreCase)
            )
                continue;
            result.Add(token);
        }

        return string.Join(' ', result);
    }

    // An initial is a single letter, optionally followed by a period ("C" or "C.").
    private static bool IsInitialToken(string token)
    {
        var bare = token.TrimEnd('.');
        return bare.Length == 1 && char.IsLetter(bare[0]);
    }

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
            transactions.AddRange(
                ExtractTransactionsFromTable(table, memberName, position, filingDate, logger)
            );
        }

        return transactions;
    }

    private static IEnumerable<DisclosureTransaction> ExtractTransactionsFromTable(
        HtmlNode table,
        string memberName,
        CongressPosition position,
        DateOnly filingDate,
        ILogger logger
    )
    {
        var headerTexts = ExtractHeaderTexts(table);
        if (headerTexts == null || !IsTransactionTable(headerTexts))
            yield break;

        var cols = MapColumnIndices(headerTexts);
        var rows = table.SelectNodes(".//tbody//tr");
        if (rows == null)
            yield break;

        foreach (var row in rows)
        {
            var tx = ParseTransactionRow(row, cols, memberName, position, filingDate, logger);
            if (tx != null)
                yield return tx;
        }
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

        string Cell(int columnIndex) => GetCleanCell(cellTexts, columnIndex);

        var txDate = Cell(cols.Date) is { } dateStr ? ParseDate(dateStr) : null;
        if (txDate == null)
            return null;

        // Senate HTML has ticker in its own column as raw text or <a> tag inner text
        var ticker = Cell(cols.Ticker);
        var assetName = Cell(cols.Asset);
        if (string.IsNullOrEmpty(ticker) && string.IsNullOrEmpty(assetName))
            return null;

        // The House PDF checkbox artifact must go before the name is stored or mined for a ticker.
        assetName = CleanAssetName(assetName);

        // Fall back to extracting ticker from asset name (parentheses pattern)
        if (string.IsNullOrEmpty(ticker) && !string.IsNullOrEmpty(assetName))
            ticker = ExtractTickerFromAssetName(assetName);

        // Skip non-stock assets when asset_type column exists
        var assetType = Cell(cols.AssetType);
        if (
            !string.IsNullOrEmpty(assetType)
            && !assetType.Contains("Stock", StringComparison.OrdinalIgnoreCase)
        )
            return null;

        var txTypeStr = Cell(cols.Type);
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

        var owner = Cell(cols.Owner);
        var amount = Cell(cols.Amount);
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

    private static string GetCleanCell(List<string> cells, int index) =>
        CleanSentinel(GetCell(cells, index));

    public static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
            return value == null ? value : string.Empty;
        return value.TruncateToFit(maxLength);
    }

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

    // Strips the PDF checkbox artifact from an asset name. The House disclosure PDFs draw each
    // row's checkboxes with a symbol font whose glyphs extract as the letter runs "gfedc" /
    // "gfedcb"; the text extractor glues those tokens onto the asset name ("Weyerhaeuser Company
    // (WY) gfedcb"). Case-sensitive on purpose — the artifact is always lowercase, and a real
    // uppercase word must never be eaten. Collapses the whitespace the removal leaves behind.
    //
    // INVARIANT: the output is stored as CongressionalTrade.AssetName, which is part of the
    // trade upsert unique key. Any change to what this method emits makes re-scraped trades
    // miss their stored rows and re-insert as duplicates (the original rollout of this cleanup
    // duplicated 8,104 production trades that way). A semantic change here MUST ship with a
    // data repair that re-normalizes and dedupes already-stored rows.
    public static string CleanAssetName(string assetName)
    {
        if (string.IsNullOrEmpty(assetName))
            return assetName;
        var cleaned = CheckboxArtifactRegex().Replace(assetName, " ");
        return RepeatedWhitespaceRegex().Replace(cleaned, " ").Trim();
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
        if (MatchesType(trimmed, ["Sale", "Sold"], "S"))
            return CongressTransactionType.Sale;
        if (MatchesType(trimmed, ["Purchase", "Buy"], "P"))
            return CongressTransactionType.Purchase;
        return null;
    }

    private static bool MatchesType(string text, string[] words, string abbreviation) =>
        words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase))
        || text.Equals(abbreviation, StringComparison.OrdinalIgnoreCase)
        || text.StartsWith(abbreviation + " ", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith(abbreviation + "(", StringComparison.OrdinalIgnoreCase);

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

    // The PDF checkbox glyph runs ("gfedc", "gfedcb") that leak into House asset names — see
    // CleanAssetName. Word-bounded so a legitimate word containing the letters is never touched.
    [GeneratedRegex(@"\bgfedcb?\b")]
    private static partial Regex CheckboxArtifactRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex RepeatedWhitespaceRegex();

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
