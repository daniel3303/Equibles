using Equibles.Integrations.Yahoo;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Equibles.SmokeTests;

// Live contract / drift-detection test. Hits the real Yahoo Finance quoteSummary API and
// asserts the INVARIANT (never exact counts) that a heavily-covered large-cap still returns
// analyst recommendation trends. GetRecommendationTrends walks
// `?.QuoteSummary?.Result?.FirstOrDefault()?.RecommendationTrend?.Trend` and returns [] if
// any link is null, so if Yahoo renames the recommendationTrend module or the trend array
// the method silently returns an empty list with no error — this fails loudly so it's
// caught before a release. Excluded from per-PR CI via "Live"; runs nightly (smoke.yml).
[Trait("Category", "Live")]
public class YahooRecommendationTrendsSmokeTests
{
    [Fact]
    public async Task GetRecommendationTrends_HeavilyCoveredLargeCap_ReturnsNonEmptyTrendsWithAnalysts()
    {
        var client = new YahooFinanceClient(
            new HttpClient(),
            NullLogger<YahooFinanceClient>.Instance
        );

        // AAPL is covered by dozens of analysts; the exact bucket counts drift weekly, but
        // the trend list can never be empty and at least one period must have a positive
        // total rating count. Asserting only those invariants stays immune to legitimate
        // value changes while still catching the silent [] fallback when the shape drifts.
        var trends = await client.GetRecommendationTrends("AAPL");

        trends
            .Should()
            .NotBeNullOrEmpty("Yahoo must still return a recommendationTrend module for AAPL");
        trends.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.Period));
        trends
            .Should()
            .OnlyContain(t =>
                t.StrongBuy >= 0 && t.Buy >= 0 && t.Hold >= 0 && t.Sell >= 0 && t.StrongSell >= 0
            );
        trends
            .Should()
            .Contain(
                t => t.StrongBuy + t.Buy + t.Hold + t.Sell + t.StrongSell > 0,
                "a heavily-covered large-cap always has analyst ratings for at least one period"
            );
    }
}
