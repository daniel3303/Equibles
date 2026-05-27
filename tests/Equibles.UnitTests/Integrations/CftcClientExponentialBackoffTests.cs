using System.Reflection;
using Equibles.Integrations.Cftc;

namespace Equibles.UnitTests.Integrations;

public class CftcClientExponentialBackoffTests
{
    // Contract (method name): exponential backoff between download retries.
    // Convention used elsewhere in this codebase (SecEdgarClient: f(n) = 2^(n+1)
    // seconds) — a one-second base that doubles per attempt: f(0)=2s, f(1)=4s,
    // f(2)=8s, f(3)=16s. The CftcClient version is the same formula.
    //
    // The risk this pin uniquely catches: ExponentialBackoff is private static
    // and untested. A "simplification" refactor that drops the `+1` shift —
    // `Math.Pow(2, attempt)` — would compile, behave plausibly (still grows
    // exponentially), and HALVE every retry interval. The CFTC public-CSV CDN
    // throttles aggressive clients with HTTP 429 responses that the inner
    // retry loop is meant to space out; halving the gap converts a transient
    // throttle into a hard ban for the worker thread, silently breaking the
    // weekly Commitments-of-Traders ingest with no exception path back to the
    // operator.
    //
    // A swap of base and exponent (`Math.Pow(attempt, 2)`) is the other plausible
    // typo: it would give f(0)=0s, f(1)=1s, f(2)=4s, f(3)=9s — sub-second early
    // retries that hammer the CDN, then a slower-than-required spread later.
    // Either regression compiles; only an assertion on a specific (attempt,
    // expected) pair catches both.
    //
    // Pick attempt=2 (expected 8 seconds, i.e. 2^3): the formula 2^(attempt+1)
    // distinguishes itself from every plausible alternative at this point:
    //   • 2^(attempt+1) → 8s  ← correct
    //   • 2^attempt     → 4s  ← halving regression
    //   • attempt^2     → 4s  ← swap regression
    //   • 2^(attempt+2) → 16s ← doubled-shift regression
    // Asserting exactly 8 seconds at attempt=2 surfaces all three.
    //
    // Reflection-invoke since the method is private static.
    [Fact]
    public void ExponentialBackoff_AttemptTwo_Returns8Seconds()
    {
        var method = typeof(CftcClient).GetMethod(
            "ExponentialBackoff",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (TimeSpan)method!.Invoke(null, [2]);

        result.Should().Be(TimeSpan.FromSeconds(8));
    }
}
