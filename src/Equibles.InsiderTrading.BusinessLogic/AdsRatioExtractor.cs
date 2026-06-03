namespace Equibles.InsiderTrading.BusinessLogic;

/// <summary>
/// Detects the ADS/ADR "unit mismatch" in a Form 4 non-derivative row and
/// returns the ordinary-shares-per-ADS ratio needed to make Shares × Price a
/// real dollar value.
///
/// Background: for issuers listed via American Depositary Shares, some Form 4
/// filers report the transaction in the issuer's <em>ordinary</em> shares (the
/// share count) but quote <c>transactionPricePerShare</c> per <em>ADS</em> (the
/// Nasdaq-traded unit). Shares (ordinary) × Price (per ADS) then overstates the
/// value by the ADS ratio — e.g. SaverOne (SVRE): 2,501,582,400 ordinary shares
/// × $3.45/ADS reads as $8.6B, when one ADS = 43,200 ordinary shares makes the
/// real value ~$200K.
///
/// The ratio and the "price is per ADS" basis both live only in free-text
/// footnotes, so this reads the row's resolved <c>Notes</c>. It corrects ONLY
/// the unambiguous mismatch and leaves every other ADS style untouched:
/// <list type="bullet">
/// <item>The security title must denote ordinary/common shares, not the ADS
/// itself — rows titled "ADSs" already pair an ADS count with an ADS price.</item>
/// <item>A footnote must state the price is per ADS, and none may state the
/// price was already converted to a per-ordinary figure.</item>
/// <item>The share count must be an exact multiple of the ratio — proof it is
/// the underlying ordinary count (ADS count × ratio), not an ADS count a filer
/// mislabeled "ordinary shares". This arithmetic test, not the prose, is what
/// separates the real bug from look-alikes (e.g. a filing whose row count is an
/// ADS count would be <em>under</em>-valued if divided, so it is left alone).</item>
/// </list>
/// Pure and stateless.
/// </summary>
public static class AdsRatioExtractor
{
    // Title denotes the ADS itself → the count is already in ADS units, so the
    // count and the per-ADS price are consistent and need no correction.
    private static readonly string[] AdsTitleMarkers =
    {
        "american depositary",
        "american depository",
        "depositary share",
        "depository share",
        "depositary receipt",
        "depository receipt",
        " ads",
        "(ads",
        "adss",
    };

    // The price is quoted per ADS — the clause that makes Shares × Price mix units.
    private static readonly string[] PerAdsPriceMarkers =
    {
        "per ads",
        "per american depositary share",
        "per american depositary receipt",
        "price of each ads",
        "per adr",
    };

    // The price was already restated to a per-ordinary figure → no correction.
    private static readonly string[] AlreadyConvertedMarkers =
    {
        "per ordinary share",
        "converted from price per ads",
        "converted from the price per ads",
        "divided by",
        "derived from the price per ads",
    };

    private static readonly Dictionary<string, int> NumberWords = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["one"] = 1,
        ["two"] = 2,
        ["three"] = 3,
        ["four"] = 4,
        ["five"] = 5,
        ["six"] = 6,
        ["seven"] = 7,
        ["eight"] = 8,
        ["nine"] = 9,
        ["ten"] = 10,
        ["eleven"] = 11,
        ["twelve"] = 12,
        ["thirteen"] = 13,
        ["fourteen"] = 14,
        ["fifteen"] = 15,
        ["sixteen"] = 16,
        ["seventeen"] = 17,
        ["eighteen"] = 18,
        ["nineteen"] = 19,
        ["twenty"] = 20,
    };

    // The token that opens a ratio clause: a bare "ads"/"adr", or the phrase
    // "american depositary share/receipt" (handled in MatchAdsRepresents). Only
    // the singular forms anchor — a plural "adss"/"adrs" tokenizes distinctly, so
    // a total-count clause ("5,655,290 ADSs representing 33,931,740 Ordinary
    // Shares") is never mistaken for the ratio.
    private static readonly HashSet<string> AdsAnchorTokens = new(StringComparer.Ordinal)
    {
        "ads",
        "adr",
    };

    private static readonly HashSet<string> RepresentsTokens = new(StringComparer.Ordinal)
    {
        "represents",
        "representing",
    };

    public static bool TryGetOrdinarySharesPerAds(
        string securityTitle,
        IReadOnlyList<string> notes,
        long shares,
        out int ratio
    )
    {
        ratio = 0;
        if (notes == null || notes.Count == 0 || shares <= 0)
            return false;

        // The count must be ordinary/common shares, not the ADS itself.
        if (TitleDenotesAds(securityTitle))
            return false;

        var pricePerAds = false;
        var alreadyConverted = false;
        int? parsedRatio = null;
        foreach (var note in notes)
        {
            if (string.IsNullOrEmpty(note))
                continue;
            var lower = note.ToLowerInvariant();
            if (PerAdsPriceMarkers.Any(marker => lower.Contains(marker)))
                pricePerAds = true;
            if (AlreadyConvertedMarkers.Any(marker => lower.Contains(marker)))
                alreadyConverted = true;
            parsedRatio ??= TryParseRatio(note);
        }

        // Need a per-ADS price, a ratio above 1, and no sign the price was
        // already converted to per-ordinary.
        if (!pricePerAds || alreadyConverted || parsedRatio is not > 1)
            return false;

        // The share count must be the underlying ordinary count: ADS count ×
        // ratio is always an exact multiple of the ratio. A row that fails this
        // is reporting an ADS count, so dividing the price would under-value it.
        if (shares % parsedRatio.Value != 0)
            return false;

        ratio = parsedRatio.Value;
        return true;
    }

    private static bool TitleDenotesAds(string securityTitle)
    {
        if (string.IsNullOrWhiteSpace(securityTitle))
            return false;
        var lower = securityTitle.ToLowerInvariant();
        return AdsTitleMarkers.Any(marker => lower.Contains(marker));
    }

    // Scan the note for an "<ADS|ADR> represent(s|ing) N ... ordinary|common
    // share(s)" clause and return N. The clause is anchored on the singular
    // ADS/ADR token so a plural count clause ("5,655,290 ADSs representing
    // 33,931,740 Ordinary Shares") never matches, and confirmed by the
    // ordinary/common-share noun that follows so an unrelated number isn't read.
    private static int? TryParseRatio(string note)
    {
        var tokens = Tokenize(note);
        for (var i = 0; i < tokens.Count; i++)
        {
            var afterRepresents = MatchAdsRepresents(tokens, i);
            if (afterRepresents < 0)
                continue;

            // The ratio is the first number within a token or two of "represents".
            var limit = Math.Min(tokens.Count, afterRepresents + 3);
            for (var j = afterRepresents + 1; j < limit; j++)
            {
                var number = ParseNumberToken(tokens[j]);
                if (number == null)
                    continue;
                if (OrdinaryShareNounFollows(tokens, j + 1))
                    return number;
                // A number that doesn't lead to the share noun isn't the ratio;
                // give up on this anchor and look for the next one.
                break;
            }
        }
        return null;
    }

    // When tokens[i] opens an ADS/ADR anchor immediately followed by
    // "represent(s|ing)", returns that verb's index; otherwise -1. Matches the
    // bare "ads"/"adr" token or the "american depositary share/receipt" phrase.
    private static int MatchAdsRepresents(IReadOnlyList<string> tokens, int i)
    {
        if (AdsAnchorTokens.Contains(tokens[i]))
            return IsRepresents(tokens, i + 1) ? i + 1 : -1;

        if (
            tokens[i] == "american"
            && i + 2 < tokens.Count
            && (tokens[i + 1] == "depositary" || tokens[i + 1] == "depository")
            && (tokens[i + 2] == "share" || tokens[i + 2] == "receipt")
        )
            return IsRepresents(tokens, i + 3) ? i + 3 : -1;

        return -1;
    }

    private static bool IsRepresents(IReadOnlyList<string> tokens, int index) =>
        index < tokens.Count && RepresentsTokens.Contains(tokens[index]);

    // True when an "ordinary"/"common" token immediately followed by
    // "share"/"shares" appears within a short window — the noun the ratio counts.
    // The window skips over interveners like "Class A", "(5)", and "of the
    // Company's" without letting an unrelated clause qualify.
    private static bool OrdinaryShareNounFollows(IReadOnlyList<string> tokens, int start)
    {
        var limit = Math.Min(tokens.Count - 1, start + 6);
        for (var i = start; i < limit; i++)
        {
            if (
                (tokens[i] == "ordinary" || tokens[i] == "common")
                && (tokens[i + 1] == "share" || tokens[i + 1] == "shares")
            )
                return true;
        }
        return false;
    }

    // Digits-with-commas ("43,200") or a number word ("twelve"). Null otherwise.
    private static int? ParseNumberToken(string token)
    {
        if (token.Length == 0)
            return null;
        if (token.Any(char.IsDigit))
            return int.TryParse(token.Replace(",", string.Empty), out var value) ? value : null;
        return NumberWords.TryGetValue(token, out var word) ? word : null;
    }

    // Lowercase, split on whitespace, and reduce each token to letters/digits and
    // the comma (kept for grouped numbers like "43,200"). Dropping the
    // surrounding punctuation lets "(ADS)", "shares." and "Company's" tokenize
    // cleanly, and leaves "ADSs" distinct from the "ads" anchor.
    private static List<string> Tokenize(string note)
    {
        var raw = note.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        var tokens = new List<string>(raw.Length);
        foreach (var word in raw)
        {
            var normalized = new string(
                word.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ',').ToArray()
            );
            if (normalized.Length > 0)
                tokens.Add(normalized);
        }
        return tokens;
    }
}
