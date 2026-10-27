using System.Text.RegularExpressions;
using Equibles.Core.Extensions;

namespace Equibles.Sec.BusinessLogic.Websites;

/// <summary>
/// Extracts the company's own website host from filing text. Regulation S-K
/// Item 101(e) requires the 10-K to disclose the registrant's website address
/// ("Our website address is www.acme.com", "available on our corporate website,
/// www.acme.com"), and DEF 14A proxies carry the same disclosure — so this reads
/// a mandated self-reported fact, not a guess. A URL only counts when its
/// immediately preceding context says it is the company's own website, which
/// excludes the SEC's, the exchange's, or any third party's URL appearing in the
/// same passage.
/// </summary>
public static partial class FilingWebsiteExtractor
{
    // Window of text before a URL that must contain a self-reference for the URL
    // to count. Disclosure phrasings keep the possessive close ("our website
    // address is", "on its corporate website,"), so a tight window keeps a
    // self-reference from leaking onto a later third-party URL in the sentence.
    private const int ContextWindow = 90;

    // How far back from an "its" the sentence naming its subject is searched for.
    private const int SentenceLookBack = 400;

    /// <summary>
    /// Returns the disclosed website host (e.g. <c>www.apple.com</c>, no scheme or
    /// path) or null when the text carries no self-referenced website. When the
    /// filing discloses both a corporate and an investor-relations address, the
    /// corporate one wins — downstream IR discovery derives the IR page from it.
    /// </summary>
    public static string Extract(string filingText)
    {
        string firstInvestorRelationsHost = null;
        foreach (var disclosure in Disclosures(filingText).Where(d => d.NamesUrl))
        {
            // Prefer the corporate host over an investor-relations one, regardless of
            // which the filing discloses first.
            if (!disclosure.InvestorRelations)
                return disclosure.Host;

            firstInvestorRelationsHost ??= disclosure.Host;
        }

        return firstInvestorRelationsHost;
    }

    /// <summary>
    /// True when the filing mentions <paramref name="host"/>'s site right after a
    /// self-reference yet never names it as the company's own: the shape a third party's
    /// URL takes ("sold through our website and Amazon.com"), which an earlier extractor
    /// stored as the company website.
    /// </summary>
    public static bool Disowns(string filingText, string host)
    {
        var site = RegistrableDomain.Of(host.ToLowerInvariant());
        var mentions = Disclosures(filingText)
            .Where(d => RegistrableDomain.Of(d.Host) == site)
            .ToList();
        return mentions.Count > 0 && !mentions.Any(d => d.NamesUrl);
    }

    // Every URL that follows a self-reference within the context window, with whether that
    // self-reference names it (see NamesThisUrl).
    private static IEnumerable<(string Host, bool NamesUrl, bool InvestorRelations)> Disclosures(
        string filingText
    )
    {
        if (string.IsNullOrWhiteSpace(filingText))
            yield break;

        foreach (Match match in UrlRegex().Matches(filingText))
        {
            var host = HostOf(match.Value);
            if (host == null || IsExcludedHost(host))
                continue;

            var contextStart = Math.Max(0, match.Index - ContextWindow);
            var context = filingText[contextStart..match.Index];
            // Classify against the self-reference nearest to the URL, not the whole
            // window: an earlier IR phrase ("our investor relations website, ir.acme.com,
            // … our company website is www.acme.com") would otherwise bleed onto the
            // corporate URL and misclassify it.
            var selfReferences = SelfReferenceRegex().Matches(context);
            if (selfReferences.Count == 0)
                continue;
            var nearest = selfReferences[^1];
            yield return (
                host,
                NamesThisUrl(filingText, contextStart, nearest, context),
                IsInvestorRelationsCandidate(host, nearest.Value)
            );
        }
    }

    /// <summary>
    /// Reduces a matched URL to its bare host: scheme and path stripped, trailing
    /// sentence punctuation removed, lower-cased. Null when nothing host-shaped
    /// remains.
    /// </summary>
    private static string HostOf(string url)
    {
        var host = url.Trim().TrimEnd('.', ',', ';', ':', ')', '"', '\'');
        var schemeIndex = host.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0)
            host = host[(schemeIndex + 3)..];
        var slashIndex = host.IndexOf('/');
        if (slashIndex >= 0)
            host = host[..slashIndex];

        return host.Contains('.') ? host.ToLowerInvariant() : null;
    }

    // Filings routinely point at the SEC's own site in the same passage
    // ("the SEC maintains a website at www.sec.gov"); never the company's.
    private static bool IsExcludedHost(string host) =>
        host == "sec.gov" || host.EndsWith(".sec.gov", StringComparison.Ordinal);

    // Whether the self-reference names this URL rather than sitting near another site's
    // ("our website and Amazon.com", "our proxy agent's website", a transfer agent's "Its
    // website is"): only connector words may separate them, no other party's possessive may
    // sit inside the phrase, and "its" counts only in a sentence naming the registrant.
    private static bool NamesThisUrl(
        string filingText,
        int contextStart,
        Match selfReference,
        string context
    )
    {
        var gap = context[(selfReference.Index + selfReference.Length)..];
        if (!ConnectorRegex().IsMatch(gap))
            return false;
        if (OtherPartyPossessiveRegex().IsMatch(selfReference.Groups["middle"].Value))
            return false;
        if (!selfReference.Groups["anchor"].Value.Equals("its", StringComparison.OrdinalIgnoreCase))
            return true;

        // The slice keeps the anchor's first letter so the boundary right before a
        // capitalised "Its" still matches its capital lookahead.
        var anchorAt = contextStart + selfReference.Index;
        var lookBack = Math.Max(0, anchorAt - SentenceLookBack);
        var preceding = filingText[lookBack..(anchorAt + 1)];
        var boundaries = SentenceBoundaryRegex().Matches(preceding);
        var sentence =
            boundaries.Count == 0
                ? preceding
                : preceding[(boundaries[^1].Index + boundaries[^1].Length)..];
        return CompanyNounRegex().IsMatch(sentence);
    }

    // An IR-flavoured candidate: the disclosure phrase called it an
    // investor-relations website, or the host itself is an investor/ir subdomain.
    private static bool IsInvestorRelationsCandidate(string host, string selfReference) =>
        selfReference.Contains("investor", StringComparison.OrdinalIgnoreCase)
        || host.StartsWith("investor", StringComparison.Ordinal)
        || host.StartsWith("ir.", StringComparison.Ordinal);

    // A URL-shaped token: optional scheme, dotted host ending in an alphabetic
    // TLD, optional path. The lookbehind keeps email-address domains out.
    [GeneratedRegex(
        @"(?<![@\w.])(?:https?://)?(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z]{2,}(?:/[^\s,;)""']*)?",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex UrlRegex();

    // The company referring to its own website just before the URL: "our website
    // address is", "on its corporate website,", "the Company's investor relations
    // website at", "We maintain a website at", "the Registrant maintains a website at", …
    // The phrase holds no second anchor, so the one nearest "website" is the one judged: "our
    // transfer agent, and its website" is an "its" phrase.
    [GeneratedRegex(
        @"(?<anchor>\bour\b|\bits\b|\bwe\b|\bthe\s+(?:company|registrant|corporation)(?:['’]s)?)(?<middle>(?:(?!\b(?:our|its|we)\b)[a-z\s,'’-]){0,40}?)(?:websites?|web\s+sites?|internet\s+(?:web\s+)?(?:site|address))",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex SelfReferenceRegex();

    // What may stand between a self-reference and the URL it names: "address is",
    // ", which can be found at", " (", "is located on the Internet at", "under the
    // investor relations link at", … Normalised filing text keeps Markdown emphasis
    // ("is *www.acme.com", "http://*www.acme.com"), so "*", "_" and a bare scheme pass too.
    [GeneratedRegex(
        @"^[\s,:;(\[""“'’*_/-]*(?:(?:address(?:es)?|is|are|at|located|currently|which|can|be|found|accessed|accessible|available|free|of|charge|following|on|in|under|for|the|company|corporation|registrant|investors?|relations|section|link|tab|page|url|internet|world|wide|web|https?)\b[\s,:;(\[""“'’*_/-]*)*$",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex ConnectorRegex();

    // Another party's possessive or pronoun inside the phrase: "our proxy agent’s website",
    // "our transfer agent is Computershare, whose website".
    [GeneratedRegex(
        @"\b(?!(?:company|registrant|corporation)['’]s\b)[a-z]+['’]s\b|\b(?:whose|their)\b",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex OtherPartyPossessiveRegex();

    // A sentence end: terminal punctuation, then whitespace and a capital, so "Inc., 480"
    // or "Inc. makes" stay inside their sentence.
    [GeneratedRegex(@"[.!?][""”’)]?\s+(?=[A-Z])")]
    private static partial Regex SentenceBoundaryRegex();

    [GeneratedRegex(
        @"\bthe\s+[""“]?(?:company|registrant|corporation|fund|trust|partnership|bank)\b",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex CompanyNounRegex();
}
