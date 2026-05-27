using System.Reflection;
using Equibles.Integrations.Yahoo;

namespace Equibles.UnitTests.Yahoo;

public class YahooFinanceClientExponentialBackoffTests
{
    // Sixth client in the ExponentialBackoff cross-client convention family
    // (Cftc, Finra, Fred, Cboe, and Sec.TransientBackoff already pinned at
    // attempt=2 → 8s). This pin covers YahooFinanceClient's private copy of
    // the same `2^(attempt+1) seconds` formula.
    //
    // Why each client needs its own pin: there is no shared retry helper —
    // every HTTP client in this codebase carries a private static copy of
    // the formula, so a refactor that touches one is the exact class of
    // edit that silently drifts the others. Pinning each copy individually
    // defends against cross-client drift.
    //
    // The risk this pin uniquely catches: YahooFinanceClient.ExponentialBackoff
    // is private static and currently untested at the unit level. A
    // "simplification" refactor that drops the `+1` shift
    // (`Math.Pow(2, attempt)`) would HALVE every retry interval. Yahoo's
    // historical-quote endpoint throttles aggressive clients with HTTP 429
    // AND silently flips to an empty-response anti-scrape mode after
    // repeated rapid hits; halving the gap accelerates the flip to silent-
    // empty, which the platform's price-import pipeline cannot distinguish
    // from "no data for this ticker" — every retried symbol on a single
    // worker iteration would lose its daily price update without any
    // exception path back to the operator. The cascade: empty Yahoo prices
    // → ValuePending=true holdings → silently-zero Holdings.Value across
    // the institutional-holdings dashboard.
    //
    // A swap of base and exponent (`Math.Pow(attempt, 2)`) is the other
    // plausible typo: f(0)=0s (immediate retry, no backoff), f(1)=1s,
    // f(2)=4s — sub-second early retries that trip Yahoo's anti-scrape
    // detector even faster.
    //
    // Pick attempt=2 (expected 8 seconds, i.e. 2^3): the formula 2^(attempt+1)
    // distinguishes itself from every plausible alternative at this point:
    //   • 2^(attempt+1) → 8s  ← correct
    //   • 2^attempt     → 4s  ← halving regression
    //   • attempt^2     → 4s  ← swap regression
    //   • 2^(attempt+2) → 16s ← doubled-shift regression
    // Asserting exactly 8 seconds at attempt=2 surfaces all three. The
    // sextet of pins (Cftc + Finra + Fred + Cboe + Sec.TransientBackoff +
    // YahooFinance — same attempt, same expected value) makes any single
    // client diverging from `2^(attempt+1)` visible at a glance.
    //
    // Reflection-invoke since the method is private static.
    [Fact]
    public void ExponentialBackoff_AttemptTwo_Returns8Seconds()
    {
        var method = typeof(YahooFinanceClient).GetMethod(
            "ExponentialBackoff",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (TimeSpan)method!.Invoke(null, [2]);

        result.Should().Be(TimeSpan.FromSeconds(8));
    }
}
