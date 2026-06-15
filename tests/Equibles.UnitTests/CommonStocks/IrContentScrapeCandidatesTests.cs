using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class IrContentScrapeCandidatesTests
{
    [Fact]
    public void ForPlatform_OrdersNeverScrapedFirstThenLeastRecentlyScraped()
    {
        // Contract: each bounded cycle must advance through the cohort, not re-scrape the
        // same head. Never-scraped stocks lead, then the oldest scrape — so the backlog
        // drains first and the cohort then refreshes oldest-first.
        var recent = Stock(
            "AAA",
            IrPlatformType.Q4Inc,
            new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc)
        );
        var old = Stock(
            "BBB",
            IrPlatformType.Q4Inc,
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        );
        var never = Stock("CCC", IrPlatformType.Q4Inc, null);

        var ordered = IrContentScrapeCandidates
            .ForPlatform(new[] { recent, old, never }.AsQueryable(), IrPlatformType.Q4Inc)
            .Select(s => s.Ticker)
            .ToList();

        ordered.Should().Equal("CCC", "BBB", "AAA");
    }

    [Fact]
    public void ForPlatform_ExcludesOtherPlatformsAndStocksWithoutIrUrl()
    {
        var match = Stock("AAA", IrPlatformType.Q4Inc, null);
        var otherPlatform = Stock("BBB", IrPlatformType.NasdaqIrInsight, null);
        var noIrUrl = Stock("CCC", IrPlatformType.Q4Inc, null, irUrl: null);

        var tickers = IrContentScrapeCandidates
            .ForPlatform(
                new[] { match, otherPlatform, noIrUrl }.AsQueryable(),
                IrPlatformType.Q4Inc
            )
            .Select(s => s.Ticker)
            .ToList();

        tickers.Should().Equal("AAA");
    }

    [Fact]
    public void ForPlatform_TieBreaksByTickerWhenScrapedAtIsEqual()
    {
        var sameTime = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var z = Stock("ZZZ", IrPlatformType.Q4Inc, sameTime);
        var a = Stock("AAA", IrPlatformType.Q4Inc, sameTime);

        var tickers = IrContentScrapeCandidates
            .ForPlatform(new[] { z, a }.AsQueryable(), IrPlatformType.Q4Inc)
            .Select(s => s.Ticker)
            .ToList();

        tickers.Should().Equal("AAA", "ZZZ");
    }

    private static CommonStock Stock(
        string ticker,
        IrPlatformType platform,
        DateTime? scrapedAt,
        string irUrl = "https://ir.example.com"
    )
    {
        return new CommonStock
        {
            Ticker = ticker,
            IrPlatformType = platform,
            InvestorRelationsUrl = irUrl,
            IrContentScrapedAt = scrapedAt,
        };
    }
}
