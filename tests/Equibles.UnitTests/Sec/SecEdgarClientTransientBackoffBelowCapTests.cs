using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientTransientBackoffBelowCapTests
{
    // Sibling to SecEdgarClientTransientBackoffCapTests. That pin protects the
    // CAP arm (`backoff > MaxRetryDelay ? MaxRetryDelay : backoff`) at attempt=10
    // → 5 min. This pin covers the structurally distinct DOUBLING arm — the
    // `2^(attempt+1) seconds` formula, sampled at attempt=2 → 8 seconds.
    //
    // Why both pins are needed when they target the same ternary:
    //   The body is `backoff = FromSeconds(Math.Pow(2, attempt + 1));
    //                return backoff > MaxRetryDelay ? MaxRetryDelay : backoff;`
    //   - CAP pin (existing, attempt=10) asserts the ternary's TRUE branch.
    //   - DOUBLING pin (this one, attempt=2) asserts the ternary's FALSE
    //     branch — that the formula's doubling output is returned VERBATIM
    //     below the cap, with no extra arithmetic.
    //
    // The risk this pin uniquely catches: a "consolidation" refactor that
    // collapsed both arms into `MaxRetryDelay` (e.g. "everything is just
    // 5 minutes" as a misguided simplification) would compile, pass the
    // existing cap-attempt=10 pin (still returns 5 min), and silently
    // INFLATE every early retry from 2-256 seconds to a full 5 minutes.
    // The SEC scraper's overall budget (10-15 min per filing cycle) would
    // burn out on the first transient DNS failure, and the scraper would
    // visibly slow to ~3 retries per filing instead of dozens.
    //
    // The complementary asymmetric risk: a refactor that swaps the
    // formula's `+1` shift (`Math.Pow(2, attempt)`) would HALVE every
    // pre-cap retry interval — caught here at attempt=2: a halving
    // regression returns 4s, this pin asserts 8s. The cap-attempt=10
    // pin can NOT catch this because the halved formula at attempt=10
    // is still 1024s — still above the cap, still returns 5 min, still
    // passes that pin.
    //
    // Picking attempt=2 also aligns this pin with the cross-client
    // ExponentialBackoff convention family (CftcClient, FinraClient,
    // FredClient, CboeClient — each pinned at attempt=2 → 8s). With this
    // pin, FIVE clients now share the same convention-pin shape, so any
    // single client diverging from the `2^(attempt+1)` standard surfaces
    // immediately on the same attempt/expected pair.
    //
    // Reflection-invoke since TransientBackoff is private static.
    [Fact]
    public void TransientBackoff_AttemptTwoBelowCap_Returns8Seconds()
    {
        var method = typeof(SecEdgarClient).GetMethod(
            "TransientBackoff",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (TimeSpan)method!.Invoke(null, [2]);

        result.Should().Be(TimeSpan.FromSeconds(8));
    }
}
