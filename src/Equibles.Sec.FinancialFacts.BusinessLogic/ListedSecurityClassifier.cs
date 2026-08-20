using System.Text.RegularExpressions;
using Equibles.CommonStocks.Data.Models;

namespace Equibles.Sec.FinancialFacts.BusinessLogic;

/// <summary>
/// Maps a security's SEC 12(b) registration title (<c>dei:Security12bTitle</c>)
/// to its <see cref="ListedSecurityType"/>. The title is the issuer's own
/// authoritative statement of what the listed security is, so this is a
/// deterministic mapping of the designated field — not a ticker or company-name
/// heuristic, which the data-accuracy rules forbid.
///
/// <para>
/// Three ordered rules, each matching whole words only:
/// <list type="number">
/// <item>Rider clauses are stripped first — parenthetical segments and
/// trailing "associated …" language ("Common Stock (and associated Preferred
/// Share Purchase Rights)") describe a poison-pill rider attached to the
/// security, not the security itself; classifying them would exclude genuine
/// common stock.</item>
/// <item>A "purchase warrants/rights/units" phrase names the security by its
/// trailing head noun ("Common Stock Purchase Warrants" is a listed warrant),
/// so that wrapper wins. The phrase requires the wrapper immediately after
/// "purchase": "Warrants to purchase Common Units" does not fire it.</item>
/// <item>Otherwise the kind whose keyword appears EARLIEST in the remaining
/// title wins — titles lead with the security's own noun and then describe
/// what they bundle ("Units, each consisting of one share of Class A Common
/// Stock and one Warrant") or convert into ("Warrants to purchase Common
/// Units").</item>
/// </list>
/// One deliberate asymmetry: bare "depositary shares" is not a common-equity
/// keyword (those receipts usually wrap a preferred series, named later in the
/// title), while "American depositary shares" is — an ADS is the US-listed
/// form of ordinary shares. Titles matching nothing map to
/// <see cref="ListedSecurityType.Other"/>, which surfaces treat exactly like
/// <see cref="ListedSecurityType.Unknown"/> — exclusion always requires
/// positive evidence.
/// </para>
/// </summary>
public static class ListedSecurityClassifier
{
    // Whole-word, case-insensitive tests. Word boundaries stop "rights" from
    // matching inside "brights" and "unit" inside "united"; singular/plural is
    // handled by matching the stem with an optional 's'.
    private static readonly (Regex Pattern, ListedSecurityType Type)[] Kinds =
    [
        (Pattern("warrants?"), ListedSecurityType.Warrants),
        (Pattern("rights?"), ListedSecurityType.Rights),
        (Pattern("units?"), ListedSecurityType.Units),
        (Pattern("preferred|preference"), ListedSecurityType.PreferredShares),
        (Pattern("notes?|debentures?|bonds?"), ListedSecurityType.DebtSecurities),
        (
            Pattern(
                "common (stock|shares?)|ordinary shares?|american depositary (shares?|receipts?)|shares? of beneficial interest"
            ),
            ListedSecurityType.CommonShares
        ),
    ];

    // Rider language: a parenthetical segment, or a clause from "associated"
    // (optionally led by ", and" / "together with") to the end of the title.
    // Both always describe something attached to the listed security.
    private static readonly Regex Parenthetical = new(
        @"\([^)]*\)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled
    );
    private static readonly Regex AssociatedTail = new(
        @"[,;]?\s+(and\s+|together\s+with\s+)?(the\s+)?associated\b.*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    // "… Purchase Warrants" / "… Purchase Rights" / "… Purchase Units": the
    // wrapper immediately after "purchase" is the title's head noun.
    private static readonly (Regex Pattern, ListedSecurityType Type)[] PurchaseWrappers =
    [
        (Pattern("purchase warrants?"), ListedSecurityType.Warrants),
        (Pattern("purchase rights?"), ListedSecurityType.Rights),
        (Pattern("purchase units?"), ListedSecurityType.Units),
    ];

    private static Regex Pattern(string body) =>
        new(
            $@"\b(?:{body})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
        );

    // The listed unit is a depositary receipt. Deliberately matched against the RAW title, not
    // the rider-stripped one Classify uses: the question here is only whether the issuer names
    // an American Depositary Share anywhere in the title it registered, and a title can put that
    // phrase inside a parenthetical ("Ordinary Shares (represented by American Depositary
    // Shares)") that Classify strips before choosing a kind.
    //
    // "Deposit[ao]ry" spells the issuer's own typo into the pattern rather than around it. The
    // canonical word is "depositary"; a handful of filers register "American Depository Shares"
    // instead, and because this is the title THEY filed there is nothing upstream to normalize
    // it. "American" is required either way, so the far commoner preferred-stock form
    // ("Depositary Shares, each representing a 1/1,000th interest in a share of Series A
    // Preferred Stock") cannot match: that one is a fractional interest in the issuer's OWN
    // preferred, counted on the same basis as the cover page, and there is no unit mismatch.
    // A Global Depositary Share carries the same mismatch and is deliberately left out: every GDS
    // issuer on record files as a foreign private issuer, so the form-based guard already answers
    // for them, and one unexercised alternation is a rule nothing pins.
    private static readonly Regex AmericanDepositary = Pattern(
        "american deposit[ao]ry (shares?|receipts?)"
    );

    /// <summary>
    /// True when the issuer's own registered title says the LISTED security is an American
    /// Depositary Share or Receipt.
    /// </summary>
    ///
    /// <remarks>
    /// A separate question from <see cref="Classify"/>, which answers what KIND of security is
    /// listed (an ADS over ordinary shares is common equity, and classifies as such). This one
    /// answers what UNIT the listing is quoted in, which is what a share count has to agree with:
    /// the depositary receipt is priced per receipt while the issuer's SEC cover page counts
    /// ordinary shares, and the two differ by the deposit ratio. Callers use it to refuse to mix
    /// the bases, never to derive the ratio — the ratio is stated in prose and reading a number
    /// out of prose is exactly what the registered title is authoritative INSTEAD of.
    /// </remarks>
    public static bool IsAmericanDepositary(string securityTitle) =>
        !string.IsNullOrWhiteSpace(securityTitle) && AmericanDepositary.IsMatch(securityTitle);

    public static ListedSecurityType Classify(string securityTitle)
    {
        if (string.IsNullOrWhiteSpace(securityTitle))
            return ListedSecurityType.Unknown;

        var title = AssociatedTail.Replace(Parenthetical.Replace(securityTitle, " "), " ");
        if (string.IsNullOrWhiteSpace(title))
            return ListedSecurityType.Other;

        foreach (var (pattern, type) in PurchaseWrappers)
        {
            if (pattern.IsMatch(title))
                return type;
        }

        var best = ListedSecurityType.Other;
        var bestIndex = int.MaxValue;
        foreach (var (pattern, type) in Kinds)
        {
            var match = pattern.Match(title);
            // Strict '<': on the (unobserved) chance two kinds match at the same
            // position, the earlier entry in Kinds wins deterministically.
            if (match.Success && match.Index < bestIndex)
            {
                bestIndex = match.Index;
                best = type;
            }
        }
        return best;
    }
}
