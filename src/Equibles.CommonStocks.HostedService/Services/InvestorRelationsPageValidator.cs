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
    /// two distinct IR content keywords. A page that declares itself a single
    /// news/press article (Open Graph <c>og:type=article</c> or an
    /// <c>article:published_time</c>/<c>article:modified_time</c> timestamp) is never
    /// an IR landing page — even when it is stuffed with IR keywords — so it is
    /// rejected up front.
    /// </summary>
    public static bool IsInvestorRelationsPage(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return false;

        var document = new HtmlDocument();
        document.LoadHtml(html);

        // A generic on-site path like /investor is often a marketing stub that
        // client-side redirects to a single press release; that article is full of
        // IR boilerplate (an "Investor Relations" contact block, "financial
        // results") and would clear the keyword check below, yet it exposes no
        // events/webcast feed and is the wrong URL to store. Open Graph article
        // metadata is the page declaring its own type, so trust it: an IR
        // landing/events hub is og:type=website, never an article.
        if (IsNewsArticle(document))
            return false;

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

    // The article: properties that mark a page as a single dated news item. Deliberately
    // narrow: article:published_time / article:modified_time are emitted per-article, whereas
    // article:publisher / article:author are injected site-wide by some CMSs (WordPress/Yoast)
    // on every page — including og:type=website homepages and IR hubs — so matching those would
    // wrongly reject genuine IR pages.
    private static readonly string[] ArticleTimestampProperties =
    [
        "article:published_time",
        "article:modified_time",
    ];

    // True when the page's own Open Graph metadata declares it a single news article:
    // og:type=article, or an article publish/modify timestamp. Both property="…" (the spec)
    // and the name="…" variant some CMSs emit are checked.
    private static bool IsNewsArticle(HtmlDocument document)
    {
        var metas = document.DocumentNode.SelectNodes("//meta");
        if (metas == null)
            return false;

        foreach (var meta in metas)
        {
            var key = (
                meta.GetAttributeValue("property", null) ?? meta.GetAttributeValue("name", "")
            ).Trim().ToLowerInvariant();

            if (ArticleTimestampProperties.Contains(key))
                return true;

            if (
                key == "og:type"
                && string.Equals(
                    meta.GetAttributeValue("content", "").Trim(),
                    "article",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return true;
        }

        return false;
    }
}
