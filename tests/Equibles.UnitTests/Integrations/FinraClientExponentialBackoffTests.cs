using System.Reflection;
using Equibles.Integrations.Finra;

namespace Equibles.UnitTests.Integrations;

public class FinraClientExponentialBackoffTests
{
    // Symmetric sibling to CftcClientExponentialBackoffTests. That pin protects
    // CftcClient's retry-backoff formula `2^(attempt+1) seconds`; this one pins
    // the structurally identical helper on FinraClient.
    //
    // Contract (method name): exponential backoff between FINRA short-interest
    // / OTC-volume API retries. Convention used across the codebase's HTTP
    // clients: f(0)=2s, f(1)=4s, f(2)=8s, f(3)=16s. Each client owns its own
    // private copy of the formula — there is no shared retry helper — so a
    // refactor that touches one is exactly the kind of edit that drifts the
    // others. Pinning each client's copy individually defends against that
    // drift class.
    //
    // The risk this pin uniquely catches: FinraClient.ExponentialBackoff is
    // private static and untested. A "simplification" refactor that drops
    // the `+1` shift (`Math.Pow(2, attempt)`) would compile, behave
    // plausibly, and HALVE every retry interval. FINRA's authenticated API
    // throttles on Bearer-token quota; halving the gap accelerates quota
    // exhaustion and converts a transient 429 into a token-revoked failure
    // for the worker, silently breaking the FINRA short-interest /
    // short-volume ingest with no exception path back to the operator.
    //
    // A swap of base and exponent (`Math.Pow(attempt, 2)`) is the other
    // plausible typo: it would give f(0)=0s (immediate retry, no backoff
    // at all), f(1)=1s, f(2)=4s — sub-second early retries that hammer
    // the authenticated endpoint, then slower-than-required spread later.
    // Either regression compiles; only an assertion on a specific (attempt,
    // expected) pair catches both.
    //
    // Pick attempt=2 (expected 8 seconds, i.e. 2^3): the formula 2^(attempt+1)
    // distinguishes itself from every plausible alternative at this point:
    //   • 2^(attempt+1) → 8s  ← correct
    //   • 2^attempt     → 4s  ← halving regression
    //   • attempt^2     → 4s  ← swap regression
    //   • 2^(attempt+2) → 16s ← doubled-shift regression
    // Asserting exactly 8 seconds at attempt=2 surfaces all three. The pair
    // of pins (CftcClient + FinraClient at the same attempt and expected
    // value) lets a reviewer see the cross-client convention at a glance.
    //
    // Reflection-invoke since the method is private static.
    [Fact]
    public void ExponentialBackoff_AttemptTwo_Returns8Seconds()
    {
        var method = typeof(FinraClient).GetMethod(
            "ExponentialBackoff",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (TimeSpan)method!.Invoke(null, [2]);

        result.Should().Be(TimeSpan.FromSeconds(8));
    }
}
