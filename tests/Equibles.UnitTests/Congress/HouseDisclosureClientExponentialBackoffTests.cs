using System.Reflection;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class HouseDisclosureClientExponentialBackoffTests
{
    // Eighth and FINAL client in the ExponentialBackoff cross-client
    // convention family. The seven sibling pins already cover:
    //   • Cftc        (PR #2259)
    //   • Finra       (PR #2260)
    //   • Fred        (PR #2261)
    //   • Cboe        (PR #2262)
    //   • Sec.Transient (PR #2263)
    //   • Yahoo       (PR #2264)
    //   • Senate      (PR #2283)
    // This pin closes the gap on the second congressional-trade client.
    // After this PR, every HTTP client in the codebase that owns a private
    // static copy of `2^(attempt+1) seconds` has an individual per-client
    // pin at attempt=2 → 8s, so a divergence on any single client surfaces
    // at the corresponding sibling.
    //
    // Why HouseDisclosureClient needs its own pin: the same drift class
    // documented in the sibling pins. There is NO shared retry helper —
    // each client owns a private static copy, so a refactor that touches
    // one is the exact edit that silently drifts the others. The House
    // disclosure PDF endpoint (disclosures-clerk.house.gov) is the
    // rate-limited public source for every Member of Congress's
    // periodic-transaction reports; halving the gap there would race
    // the worker into the throttle state and silently break the House
    // congressional-trade ingest cycle (zero new transactions per
    // iteration, no exception path back to the operator).
    //
    // Pick attempt=2 (expected 8 seconds, i.e. 2^3): the formula
    // 2^(attempt+1) distinguishes itself from every plausible
    // alternative at this point:
    //   • 2^(attempt+1) → 8s  ← correct
    //   • 2^attempt     → 4s  ← halving regression
    //   • attempt^2     → 4s  ← swap regression
    //   • 2^(attempt+2) → 16s ← doubled-shift regression
    // The OCTET of pins (eight clients, same attempt, same expected
    // value) makes any divergence on any single client visible at a
    // glance across the convention pin set.
    //
    // Reflection-invoke since the method is private static.
    [Fact]
    public void ExponentialBackoff_AttemptTwo_Returns8Seconds()
    {
        var method = typeof(HouseDisclosureClient).GetMethod(
            "ExponentialBackoff",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (TimeSpan)method!.Invoke(null, [2]);

        result.Should().Be(TimeSpan.FromSeconds(8));
    }
}
