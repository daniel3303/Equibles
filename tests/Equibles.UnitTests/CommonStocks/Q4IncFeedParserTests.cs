using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.HostedService.Services;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.CommonStocks;

// Record-replay: parse frozen Q4 Inc RSS fixtures and assert exact values.
// A diff here means the parser regressed against the recorded feed shape.
public class Q4IncFeedParserTests
{
    private static string NewsFeed() =>
        File.ReadAllText("TestAssets/InvestorRelations/q4-news.xml");

    private static string EventsFeed() =>
        File.ReadAllText("TestAssets/InvestorRelations/q4-events.xml");

    [Fact]
    public void ParseNews_Q4Feed_MapsFirstItemExactly()
    {
        var items = Q4IncFeedParser.ParseNews(NewsFeed());

        // The sixth fixture item has no pubDate and must be skipped, never guessed.
        items.Should().HaveCount(5);
        var first = items[0];
        first.Title.Should().Be("Amazon.com Announces First Quarter Results");
        first
            .Url.Should()
            .Be(
                "https://ir.aboutamazon.com/news-release/news-release-details/2026/Amazon-com-Announces-First-Quarter-Results/default.aspx"
            );
        // pubDate "Wed, 29 Apr 2026 16:01:00 -0400" must normalise to UTC.
        first.PublishedAtUtc.Should().Be(new DateTime(2026, 4, 29, 20, 1, 0, DateTimeKind.Utc));
        first
            .Summary.Should()
            .Be(
                "Amazon.com, Inc. today announced financial results for its first quarter ended March 31, 2026."
            );
    }

    [Fact]
    public void ParseNews_EmptyDescription_YieldsNullSummary()
    {
        var items = Q4IncFeedParser.ParseNews(NewsFeed());

        var noSummary = items[2];
        noSummary
            .Title.Should()
            .Be("Amazon and the National Basketball Association Tip Off New Global Streaming Era");
        noSummary.Summary.Should().BeNull();
    }

    [Fact]
    public void ParseEvents_TakesStartDateFromTitlePrefix_NotPubDate()
    {
        var events = Q4IncFeedParser.ParseEvents(EventsFeed());

        var first = events[0];
        // Title prefix "6/10/2026 : " carries the event date; pubDate (June 2) is the
        // publication time and must NOT be used as the start.
        first.Title.Should().Be("Q4 FY26 Earnings");
        first.StartDateTimeUtc.Should().Be(new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc));
        first.Type.Should().Be(IrEventType.EarningsCall);
        first
            .Url.Should()
            .Be(
                "https://investor.oracle.com/events-and-presentations/event-details/2026/Q4-FY26-Earnings/default.aspx"
            );
    }

    [Fact]
    public void ParseEvents_PrefixWithTime_ParsesDateAndTime()
    {
        var events = Q4IncFeedParser.ParseEvents(EventsFeed());

        var investorDay = events[2];
        investorDay.Title.Should().Be("Investor Day");
        investorDay
            .StartDateTimeUtc.Should()
            .Be(new DateTime(2026, 9, 15, 14, 30, 0, DateTimeKind.Utc));
        investorDay.Type.Should().Be(IrEventType.Presentation);
    }

    [Fact]
    public void ParseEvents_MissingOrUnparseableDatePrefix_SkipsItem()
    {
        var events = Q4IncFeedParser.ParseEvents(EventsFeed());

        // Of the five fixture items, the one without a " : " separator and the one
        // with an unparseable date prefix are both skipped rather than guessed.
        events.Should().HaveCount(3);
        events.Should().NotContain(e => e.Title.Contains("Annual Meeting"));
        events.Should().NotContain(e => e.Title.Contains("unparseable"));
    }

    [Fact]
    public void ParseNews_MalformedXml_ReturnsEmptyWithoutThrowing()
    {
        var items = Q4IncFeedParser.ParseNews("<rss><channel><item>broken");

        items.Should().BeEmpty();
    }
}
