using System.Text.RegularExpressions;

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

    /// <summary>
    /// Returns the disclosed website host (e.g. <c>www.apple.com</c>, no scheme or
    /// path) or null when the text carries no self-referenced website. When the
    /// filing discloses both a corporate and an investor-relations address, the
    /// corporate one wins — downstream IR discovery derives the IR page from it.
    /// </summary>
    public static string Extract(string filingText)
    {
        if (string.IsNullOrWhiteSpace(filingText))
            return null;

        string firstInvestorRelationsHost = null;
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

            // Prefer the corporate host over an investor-relations one, regardless of
            // which the filing discloses first.
            if (!IsInvestorRelationsCandidate(host, selfReferences[^1].Value))
                return host;

            firstInvestorRelationsHost ??= host;
        }

        return firstInvestorRelationsHost;
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
    // website at", "the Registrant maintains a website at", …
    [GeneratedRegex(
        @"(?:\bour\b|\bits\b|company(?:['’]s)?|registrant(?:['’]s)?|corporation(?:['’]s)?)[a-z\s,'’-]{0,40}?(?:website|web\s+site|internet\s+(?:web\s+)?(?:site|address))",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex SelfReferenceRegex();
}
