using System.Reflection;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class SenateDisclosureClientExponentialBackoffTests
{
    // Seventh client in the ExponentialBackoff cross-client convention family
    // (Cftc #2259, Finra #2260, Fred #2261, Cboe #2262, Sec.TransientBackoff
    // #2263, YahooFinance #2264 already pinned at attempt=2 → 8s). This pin
    // covers SenateDisclosureClient's private copy of the same
    // `2^(attempt+1) seconds` formula.
    //
    // Why this client also needs its own pin: there is no shared retry
    // helper — every HTTP client carries a private static copy, so a
    // refactor that touches one is the exact class of edit that silently
    // drifts the others. Pinning each copy individually defends against
    // cross-client drift; the seventh pin closes the visible gap on the
    // congressional-trade ingest side.
    //
    // The risk this pin uniquely catches: SenateDisclosureClient.
    // ExponentialBackoff is private static and currently untested at the
    // unit level. A "simplification" refactor that drops the `+1` shift
    // (`Math.Pow(2, attempt)`) would HALVE every retry interval. The
    // Senate efdsearch.senate.gov endpoint is rate-limited and serves
    // an opaque JSON challenge under aggressive load; halving the gap
    // accelerates the trip to that challenge state, after which the
    // session is effectively unusable for the rest of the worker cycle.
    // The downstream impact: every Senate congressional-trade ingest
    // cycle silently produces zero new rows.
    //
    // A swap of base and exponent (`Math.Pow(attempt, 2)`) is the other
    // plausible typo: f(0)=0s (immediate retry, no backoff at all),
    // f(1)=1s, f(2)=4s — sub-second early retries that hit the rate
    // limiter even faster.
    //
    // Pick attempt=2 (expected 8 seconds, i.e. 2^3): the formula
    // 2^(attempt+1) distinguishes itself from every plausible alternative
    // at this point:
    //   • 2^(attempt+1) → 8s  ← correct
    //   • 2^attempt     → 4s  ← halving regression
    //   • attempt^2     → 4s  ← swap regression
    //   • 2^(attempt+2) → 16s ← doubled-shift regression
    // Asserting exactly 8 seconds at attempt=2 surfaces all three. With
    // this pin, SEVEN clients now share the same convention shape; any
    // single client diverging from `2^(attempt+1)` is visible at a
    // glance across the convention's pin files.
    //
    // Reflection-invoke since the method is private static.
    [Fact]
    public void ExponentialBackoff_AttemptTwo_Returns8Seconds()
    {
        var method = typeof(SenateDisclosureClient).GetMethod(
            "ExponentialBackoff",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (TimeSpan)method!.Invoke(null, [2]);

        result.Should().Be(TimeSpan.FromSeconds(8));
    }
}
