using System.Reflection;
using Equibles.Integrations.Fred;

namespace Equibles.UnitTests.Integrations;

public class FredClientExponentialBackoffTests
{
    // Third in the ExponentialBackoff cross-client convention family
    // (CftcClient, FinraClient already pinned). This one covers FredClient's
    // private copy of the same `2^(attempt+1) seconds` formula.
    //
    // Why each client needs its own pin: there is no shared retry helper —
    // every HTTP client carries a private static copy of the formula, so a
    // refactor that touches one is the exact class of edit that silently
    // drifts the others. Pinning each copy individually defends against
    // cross-client drift.
    //
    // The risk this pin uniquely catches: FredClient.ExponentialBackoff is
    // private static and untested. A "simplification" refactor that drops
    // the `+1` shift (`Math.Pow(2, attempt)`) would HALVE every retry
    // interval. FRED's free API throttles aggressive clients with HTTP 429
    // (their published quota is 120 requests/min); halving the gap
    // accelerates quota exhaustion and converts a transient throttle into
    // a hard ban for the worker thread, silently breaking the daily
    // macro-indicator ingest (CPI, unemployment, GDP, etc) with no
    // exception path back to the operator. The downstream impact is
    // visible — economic-data dashboards and any MCP-tool query
    // (GetEconomicIndicator, SearchEconomicIndicators) would silently
    // return stale data days behind the public FRED release schedule.
    //
    // A swap of base and exponent (`Math.Pow(attempt, 2)`) is the other
    // plausible typo: f(0)=0s (immediate retry, no backoff), f(1)=1s,
    // f(2)=4s — sub-second early retries that hammer FRED and burn
    // through the quota in a fraction of a minute.
    //
    // Pick attempt=2 (expected 8 seconds, i.e. 2^3): the formula 2^(attempt+1)
    // distinguishes itself from every plausible alternative at this point:
    //   • 2^(attempt+1) → 8s  ← correct
    //   • 2^attempt     → 4s  ← halving regression
    //   • attempt^2     → 4s  ← swap regression
    //   • 2^(attempt+2) → 16s ← doubled-shift regression
    // Asserting exactly 8 seconds at attempt=2 surfaces all three. The triad
    // of pins (CftcClient + FinraClient + FredClient at the same attempt
    // and expected value) lets a reviewer see the cross-client convention
    // at a glance and surfaces drift the moment any single client diverges.
    //
    // Reflection-invoke since the method is private static.
    [Fact]
    public void ExponentialBackoff_AttemptTwo_Returns8Seconds()
    {
        var method = typeof(FredClient).GetMethod(
            "ExponentialBackoff",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (TimeSpan)method!.Invoke(null, [2]);

        result.Should().Be(TimeSpan.FromSeconds(8));
    }
}
