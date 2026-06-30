using HtmlAgilityPack;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Extracts candidate investor-relations URLs from a company homepage by reading its
/// anchors — the link whose visible text or href is about investor relations. This catches
/// IR pages at locations path/subdomain guessing can't reach: a different host (a Q4 / GCS
/// portal), a regional subdomain, a locale path (<c>/ja/ir/</c>), or a deeper path
/// (<c>/investor-information</c>, <c>/stock-info/</c>). Pure (no I/O), so it is unit-testable
/// against fixture HTML.
/// </summary>
public static class InvestorRelationsLinkExtractor
{
    // Strong IR signals in the anchor text or the href.
    private static readonly string[] PrimaryKeywords = ["investor", "shareholder"];

    // Weaker signals SPACs / microcaps use instead of an "Investors" link (a SEC-filings or
    // stock-information link). These are validated downstream, so a false positive costs only a
    // probe — they are tried after every primary candidate.
    private static readonly string[] SecondaryKeywords =
    [
        "sec filings",
        "sec-filings",
        "stock information",
        "stock-info",
        "stock quote",
        "financial reports",
        "financial-reports",
        "annual report",
    ];

    /// <summary>
    /// Returns absolute IR-link candidates found in <paramref name="html"/>, resolved against
    /// <paramref name="baseUrl"/>, primary signals first, de-duplicated, capped at
    /// <paramref name="max"/>. Empty when the HTML has no anchors or the base URL is unparseable.
    /// </summary>
    public static IReadOnlyList<string> Extract(string html, string baseUrl, int max = 6)
    {
        if (
            string.IsNullOrWhiteSpace(html)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
        )
            return [];

        var document = new HtmlDocument();
        document.LoadHtml(html);
        var anchors = document.DocumentNode.SelectNodes("//a[@href]");
        if (anchors == null)
            return [];

        var primary = new List<string>();
        var secondary = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var anchor in anchors)
        {
            if (!TryResolveHref(baseUri, anchor.GetAttributeValue("href", ""), out var absolute))
                continue;

            var text = HtmlEntity.DeEntitize(anchor.InnerText ?? "").Trim().ToLowerInvariant();
            var haystack = text + " " + absolute.ToLowerInvariant();

            // De-duplicate among the candidates only — recording the dedup key here, after
            // classification, means a non-IR anchor to a URL can't consume the slot and suppress
            // a later IR anchor to that same URL (GH-3957).
            if (PrimaryKeywords.Any(haystack.Contains) || HasIrHostOrPath(absolute))
            {
                if (seen.Add(absolute))
                    primary.Add(absolute);
            }
            else if (SecondaryKeywords.Any(haystack.Contains))
            {
                if (seen.Add(absolute))
                    secondary.Add(absolute);
            }
        }

        // Among the primary candidates prefer the shallowest path, so an IR landing / overview page
        // outranks a deep article / press-release detail page on the same IR host — downstream takes
        // the first candidate that validates, and a deep "latest news" article validates too
        // (GH-5018). OrderBy is stable, so equal-depth candidates keep their document order.
        return primary.OrderBy(PathDepth).Concat(secondary).Take(max).ToList();
    }

    // Number of non-empty path segments, used to rank a shallow IR landing above a deep article.
    private static int PathDepth(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length
            : int.MaxValue;

    // The link's host is an ir. / investor(s). subdomain, or its path carries an "ir" segment
    // (e.g. a locale-prefixed /ja/ir/) — strong IR signals even when the anchor text isn't English.
    private static bool HasIrHostOrPath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("ir.") || host.StartsWith("investor.") || host.StartsWith("investors."))
            return true;

        return uri
            .AbsolutePath.ToLowerInvariant()
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Contains("ir");
    }

    private static bool TryResolveHref(Uri baseUri, string href, out string absolute)
    {
        absolute = null;
        if (string.IsNullOrWhiteSpace(href))
            return false;

        var trimmed = href.Trim();
        if (
            trimmed.StartsWith("#")
            || trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
        )
            return false;

        if (
            !Uri.TryCreate(baseUri, trimmed, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        )
            return false;

        // EDGAR is the regulator's site, not a company IR page — filings already come from there.
        var host = uri.Host.ToLowerInvariant();
        if (host == "sec.gov" || host.EndsWith(".sec.gov"))
            return false;

        // Drop the fragment; keep the path and query.
        absolute = uri.GetLeftPart(UriPartial.Query);
        return true;
    }
}
