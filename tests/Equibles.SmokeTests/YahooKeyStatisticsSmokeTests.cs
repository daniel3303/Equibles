using Equibles.Integrations.Yahoo;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Equibles.SmokeTests;

// Live contract / drift-detection test. Hits the real Yahoo Finance quoteSummary API and
// asserts the INVARIANT (never an exact value) that a real large-cap reports positive shares
// outstanding. GetKeyStatistics maps via `stats.SharesOutstanding?.Raw ?? 0`, so if Yahoo
// renames the defaultKeyStatistics module or the sharesOutstanding.raw field the parser
// silently returns 0 with no error — this test fails loudly so it's caught before a release.
// Excluded from per-PR CI via the "Live" category; runs nightly (smoke.yml).
[Trait("Category", "Live")]
public class YahooKeyStatisticsSmokeTests
{
    [Fact]
    public async Task GetKeyStatistics_KnownLargeCap_ReturnsPositiveSharesOutstanding()
    {
        var client = new YahooFinanceClient(
            new HttpClient(),
            NullLogger<YahooFinanceClient>.Instance
        );

        // Apple has had billions of shares outstanding for decades — the exact figure
        // drifts with buybacks/splits, but it can never be <= 0 for a live large-cap.
        // Asserting only the sign keeps this immune to legitimate value changes while
        // still catching the silent `?? 0` fallback when the payload shape drifts.
        var stats = await client.GetKeyStatistics("AAPL");

        stats.Should().NotBeNull("Yahoo must still return a defaultKeyStatistics module for AAPL");
        stats
            .SharesOutstanding.Should()
            .BeGreaterThan(0, "a live large-cap always reports positive shares outstanding");
    }
}
