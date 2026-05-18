using Equibles.Integrations.Yahoo;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Equibles.SmokeTests;

// Live contract / drift-detection test. Hits the real Yahoo Finance API and asserts the
// SHAPE and INVARIANTS of the response — never exact values — for an immutable past window.
// Its only job is to fail loudly when Yahoo changes its payload so the parser can be fixed
// before a release. Excluded from per-PR CI via the "Live" category; runs nightly (smoke.yml).
[Trait("Category", "Live")]
public class YahooHistoricalPricesSmokeTests
{
    [Fact]
    public async Task GetHistoricalPrices_KnownPastWindow_ReturnsWellFormedDailyBars()
    {
        var client = new YahooFinanceClient(
            new HttpClient(),
            NullLogger<YahooFinanceClient>.Instance
        );
        // January 2024 is closed and immutable: ~21 NYSE trading days. We assert a lower
        // bound (>= 15) rather than an exact count so a single missing/holiday bar upstream
        // doesn't make this flaky — drift in the response shape is what we want to catch.
        var start = new DateOnly(2024, 1, 2);
        var end = new DateOnly(2024, 1, 31);

        var prices = await client.GetHistoricalPrices("AAPL", start, end);

        prices.Should().NotBeNullOrEmpty("Yahoo must still return daily bars for AAPL");
        prices.Count.Should().BeGreaterThanOrEqualTo(15, "January 2024 had ~21 trading days");

        prices.Should().OnlyContain(p => p.Date >= start && p.Date <= end);
        prices.Should().OnlyContain(p => p.High >= p.Low);
        prices.Should().OnlyContain(p => p.High >= p.Open && p.High >= p.Close);
        prices.Should().OnlyContain(p => p.Low <= p.Open && p.Low <= p.Close);
        prices.Should().OnlyContain(p => p.Close > 0 && p.AdjustedClose > 0);
        prices.Should().OnlyContain(p => p.Volume > 0);
    }
}
