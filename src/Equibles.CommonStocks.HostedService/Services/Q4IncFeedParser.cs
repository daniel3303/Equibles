using System.Globalization;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Parses the RSS feeds a Q4 Inc-hosted IR site publishes — press releases
/// (<c>/rss/pressrelease.aspx</c>) and events (<c>/rss/event.aspx</c>) — into typed
/// items. Pure (no I/O) so it is unit-testable against recorded feed fixtures.
/// Items missing a required field or carrying an unparseable date are skipped
/// rather than guessed, to keep the persisted data trustworthy.
/// </summary>
public static class Q4IncFeedParser
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
            if (rawTitle == null || url == null)
                continue;

            // Q4 event titles arrive as "6/23/2026 : FedEx Q4 FY26 Earnings Call".
            // Unlike Nasdaq IR Insight, pubDate is the publication time, not the event
            // start — the authoritative start date is this title prefix, so an item
            // whose prefix can't be parsed is skipped rather than guessed.
            var separator = rawTitle.IndexOf(" : ", StringComparison.Ordinal);
            if (separator < 0)
                continue;

            var start = ParseEventStart(rawTitle[..separator]);
            var title = IrRssFeed.Trim(rawTitle[(separator + 3)..], IrRssFeed.MaxTitle);
            if (start == null || title == null)
                continue;

            events.Add(
                new ParsedIrEvent(title, url, start.Value, IrEventClassifier.Classify(title))
            );
        }

        return events;
    }

    // The prefix is a bare US-style date ("6/23/2026"), occasionally with a time.
    // No timezone is published for it, so it is taken as UTC.
    private static DateTime? ParseEventStart(string prefix)
    {
        string[] formats = ["M/d/yyyy h:mm tt", "M/d/yyyy"];
        if (
            DateTime.TryParseExact(
                prefix.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed
            )
        )
        {
            return parsed;
        }

        return null;
    }
}
