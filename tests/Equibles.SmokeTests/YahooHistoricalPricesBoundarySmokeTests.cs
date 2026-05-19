using Equibles.Integrations.Yahoo;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Equibles.SmokeTests;

// Live contract / drift-detection test for the BOUNDARY case of GetHistoricalPrices.
// The existing mid-month smoke test never exercises the timezone over-fetch/trim path:
// the client deliberately over-fetches [start-1d, end+2d] in UTC then trims to the exact
// exchange-local [start, end] window. A single-trading-day request is the tightest probe
// of that arithmetic — it must return exactly one bar stamped on that exact date, never
// drop the boundary day, never leak an adjacent day. Fails loudly if Yahoo's timestamp
// stamping or the trim logic drifts. Excluded from per-PR CI ("Live"); nightly (smoke.yml).
[Trait("Category", "Live")]
public class YahooHistoricalPricesBoundarySmokeTests
{
    [Fact]
    public async Task GetHistoricalPrices_SingleKnownTradingDay_ReturnsExactlyThatDaysBar()
    {
        var client = new YahooFinanceClient(
            new HttpClient(),
            NullLogger<YahooFinanceClient>.Instance
        );

        // 2024-01-03 is an immutable, ordinary NYSE trading day (Wed; Jan 1 was the
        // holiday, Jan 2 & 3 traded). Requesting exactly [03,03] must yield one bar
        // dated 2024-01-03 — the over-fetch must be trimmed back to the exact day.
        var day = new DateOnly(2024, 1, 3);

        var prices = await client.GetHistoricalPrices("AAPL", day, day);

        prices.Should().ContainSingle("a single trading-day window must trim to exactly one bar");
        prices[0]
            .Date.Should()
            .Be(day, "the boundary day must be stamped on the exchange-local date");
        prices[0].High.Should().BeGreaterThanOrEqualTo(prices[0].Low);
        prices[0].Close.Should().BeGreaterThan(0);
        prices[0].Volume.Should().BeGreaterThan(0);
    }
}
