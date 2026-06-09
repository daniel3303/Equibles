using HtmlAgilityPack;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Decides whether fetched HTML is actually an investor-relations page rather
/// than a soft-404 or a homepage a guessed path redirected to. Pure logic (no
/// I/O) so it is unit-testable against fixture HTML.
/// </summary>
public static class InvestorRelationsPageValidator
{
    // A page title carrying one of these is a strong signal on its own — IR pages
    // almost always say so in the <title>.
    private static readonly string[] TitlePhrases =
    [
        "investor relations",
        "investor relation",
        "investors",
        "shareholder",
        "ir home",
    ];

    // Content keywords. A homepage may mention "investor relations" once in a nav
    // footer, so two distinct hits are required when the title alone isn't decisive.
    private static readonly string[] BodyKeywords =
    [
        "investor relations",
        "sec filings",
        "quarterly results",
        "quarterly earnings",
        "annual report",
        "press releases",
        "shareholder",
        "earnings release",
        "financial results",
        "stock information",
        "annual meeting",
        "dividend history",
    ];

    /// <summary>
    /// True when <paramref name="html"/> looks like an investor-relations page:
    /// the page title carries an IR phrase, or the visible text contains at least
    /// two distinct IR content keywords.
    /// </summary>
    public static bool IsInvestorRelationsPage(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return false;

        var document = new HtmlDocument();
        document.LoadHtml(html);

        // Drop script/style so their contents don't count as "visible" keywords.
        var noise = document.DocumentNode.SelectNodes("//script|//style");
        if (noise != null)
        {
            foreach (var node in noise)
                node.Remove();
        }

        var title =
            document.DocumentNode.SelectSingleNode("//title")?.InnerText?.ToLowerInvariant() ?? "";
        if (TitlePhrases.Any(title.Contains))
            return true;

        var text = HtmlEntity.DeEntitize(document.DocumentNode.InnerText ?? "").ToLowerInvariant();
        var distinctHits = BodyKeywords.Count(text.Contains);
        return distinctHits >= 2;
    }
}
