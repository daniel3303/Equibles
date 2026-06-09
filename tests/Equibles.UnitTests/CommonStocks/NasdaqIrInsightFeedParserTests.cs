using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.HostedService.Services;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.CommonStocks;

// Record-replay: parse frozen Nasdaq IR Insight RSS fixtures and assert exact values.
// A diff here means the parser regressed against the recorded feed shape.
public class NasdaqIrInsightFeedParserTests
{
    private static string NewsFeed() =>
        File.ReadAllText("TestAssets/InvestorRelations/nasdaq-news.xml");

    private static string EventsFeed() =>
        File.ReadAllText("TestAssets/InvestorRelations/nasdaq-events.xml");

    [Fact]
    public void ParseNews_NasdaqFeed_MapsFirstItemExactly()
    {
        var items = NasdaqIrInsightFeedParser.ParseNews(NewsFeed());

        items.Should().HaveCount(10);
        var first = items[0];
        first
            .Title.Should()
            .Be("Nasdaq Launches Economic Institute, Debuts New AI Research Series");
        first
            .Url.Should()
            .Be(
                "https://ir.nasdaq.com/news-releases/news-release-details/nasdaq-launches-economic-institute-debuts-new-ai-research-series"
            );
        // pubDate "Tue, 09 Jun 2026 06:00:00 -0400" must normalise to UTC.
        first.PublishedAtUtc.Should().Be(new DateTime(2026, 6, 9, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ParseEvents_StripsDatePrefixAndNormalisesPubDateToUtc()
    {
        var events = NasdaqIrInsightFeedParser.ParseEvents(EventsFeed());

        events.Should().HaveCount(10);
        var first = events[0];
        // Title prefix "June 9, 2026 8:15 AM EDT : " must be stripped to the clean label.
        first.Title.Should().Be("Morgan Stanley US Financials Conference");
        // The authoritative start time comes from pubDate (08:15 -0400 -> 12:15 UTC),
        // never from re-parsing the title prefix.
        first.StartDateTimeUtc.Should().Be(new DateTime(2026, 6, 9, 12, 15, 0, DateTimeKind.Utc));
        first.Type.Should().Be(IrEventType.Conference);
    }

    [Fact]
    public void ParseNews_MalformedXml_ReturnsEmptyWithoutThrowing()
    {
        var items = NasdaqIrInsightFeedParser.ParseNews("<rss><channel><item>broken");

        items.Should().BeEmpty();
    }
}
