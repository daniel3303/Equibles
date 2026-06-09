using System.Globalization;
using System.Xml.Linq;
using Equibles.CommonStocks.Data.Models;

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
    // Column ceilings on IrNewsItem / IrEvent. Defensive truncation so an unusually
    // long source value can't fail the insert.
    private const int MaxTitle = 512;
    private const int MaxUrl = 1024;
    private const int MaxSummary = 4000;

    public static IReadOnlyList<ParsedIrNewsItem> ParseNews(string xml)
    {
        var items = new List<ParsedIrNewsItem>();
        foreach (var item in Items(xml))
        {
            var title = Trim(Value(item, "title"), MaxTitle);
            var url = Trim(Value(item, "link"), MaxUrl);
            var published = ParseDate(Value(item, "pubDate"));
            if (title == null || url == null || published == null)
                continue;

            var summary = Trim(Value(item, "description"), MaxSummary);
            items.Add(new ParsedIrNewsItem(title, url, summary, published.Value));
        }

        return items;
    }

    public static IReadOnlyList<ParsedIrEvent> ParseEvents(string xml)
    {
        var events = new List<ParsedIrEvent>();
        foreach (var item in Items(xml))
        {
            var rawTitle = Value(item, "title");
            var url = Trim(Value(item, "link"), MaxUrl);
            var start = ParseDate(Value(item, "pubDate"));
            var title = Trim(CleanEventTitle(rawTitle), MaxTitle);
            if (title == null || url == null || start == null)
                continue;

            events.Add(new ParsedIrEvent(title, url, start.Value, ClassifyEventType(title)));
        }

        return events;
    }

    private static IEnumerable<XElement> Items(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return [];

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        return document.Descendants("item");
    }

    private static string Value(XElement item, string name)
    {
        var text = item.Element(name)?.Value;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
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

    // Maps the event's own label to a normalised kind. Unknown when no label
    // matches — never a guess. (See IrEventType.)
    private static IrEventType ClassifyEventType(string title)
    {
        var t = title.ToLowerInvariant();
        if (t.Contains("earnings"))
            return IrEventType.EarningsCall;
        if (t.Contains("annual meeting") || t.Contains("shareholder") || t.Contains("stockholder"))
            return IrEventType.ShareholderMeeting;
        if (t.Contains("conference"))
            return IrEventType.Conference;
        if (t.Contains("investor day") || t.Contains("analyst day") || t.Contains("presentation"))
            return IrEventType.Presentation;
        if (t.Contains("webcast"))
            return IrEventType.Webcast;
        return IrEventType.Unknown;
    }

    private static DateTime? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (
            DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed
            )
        )
        {
            return parsed.UtcDateTime;
        }

        return null;
    }

    private static string Trim(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }
}
