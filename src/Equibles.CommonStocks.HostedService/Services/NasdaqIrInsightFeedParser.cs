namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Parses the RSS feeds a Nasdaq IR Insight-hosted IR site publishes — news
/// releases (<c>/rss/news-releases.xml</c>) and events (<c>/rss/events.xml</c>) —
/// into typed items. Pure (no I/O) so it is unit-testable against recorded feed
/// fixtures. Items missing a required field or carrying an unparseable date are
/// skipped rather than guessed, to keep the persisted data trustworthy.
/// </summary>
public static class NasdaqIrInsightFeedParser
{
    public static IReadOnlyList<ParsedIrNewsItem> ParseNews(string xml)
    {
        var items = new List<ParsedIrNewsItem>();
        foreach (var item in IrRssFeed.Items(xml))
        {
            var title = IrRssFeed.Trim(IrRssFeed.Value(item, "title"), IrRssFeed.MaxTitle);
            var url = IrRssFeed.Trim(IrRssFeed.Value(item, "link"), IrRssFeed.MaxUrl);
            var published = IrRssFeed.ParseDate(IrRssFeed.Value(item, "pubDate"));
            if (title == null || url == null || published == null)
                continue;

            var summary = IrRssFeed.Trim(
                IrRssFeed.Value(item, "description"),
                IrRssFeed.MaxSummary
            );
            items.Add(new ParsedIrNewsItem(title, url, summary, published.Value));
        }

        return items;
    }

    public static IReadOnlyList<ParsedIrEvent> ParseEvents(string xml)
    {
        var events = new List<ParsedIrEvent>();
        foreach (var item in IrRssFeed.Items(xml))
        {
            var rawTitle = IrRssFeed.Value(item, "title");
            var url = IrRssFeed.Trim(IrRssFeed.Value(item, "link"), IrRssFeed.MaxUrl);
            var start = IrRssFeed.ParseDate(IrRssFeed.Value(item, "pubDate"));
            var title = IrRssFeed.Trim(CleanEventTitle(rawTitle), IrRssFeed.MaxTitle);
            if (title == null || url == null || start == null)
                continue;

            events.Add(
                new ParsedIrEvent(title, url, start.Value, IrEventClassifier.Classify(title))
            );
        }

        return events;
    }

    // Event titles arrive prefixed with the date, e.g.
    // "June 9, 2026 8:15 AM EDT : Morgan Stanley US Financials Conference".
    // Keep only the human label after the " : " separator; the authoritative start
    // time comes from pubDate, never from re-parsing this prefix.
    private static string CleanEventTitle(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
            return null;

        var separator = rawTitle.IndexOf(" : ", StringComparison.Ordinal);
        var label = separator >= 0 ? rawTitle[(separator + 3)..] : rawTitle;
        label = label.Trim();
        return label.Length == 0 ? null : label;
    }
}
