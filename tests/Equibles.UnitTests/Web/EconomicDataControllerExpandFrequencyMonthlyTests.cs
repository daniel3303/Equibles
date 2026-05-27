using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class EconomicDataControllerExpandFrequencyMonthlyTests
{
    // Final pin in the ExpandFrequency family. After this PR every explicit
    // arm has individual per-arm coverage:
    //   D / W / BW / M / Q / SA / A → human labels
    //   Lowercase input → normalised via ToUpperInvariant (existing pin)
    //   Unknown → input echoed (existing pin)
    //
    // This pin covers M → Monthly via the EXPLICIT uppercase arm — the
    // existing lowercase-normalisation pin asserts that "m" routes through
    // ToUpperInvariant to the M arm, but it can't distinguish:
    //   • Working M arm: BOTH "M" and "m" reach the same path and return
    //     "Monthly".
    //   • Drop-the-M-arm regression: "M" falls into default (returns "M"
    //     verbatim), AND "m" — after .ToUpperInvariant() → "M" — also falls
    //     into default and returns "M" verbatim. The existing
    //     lowercase-normalisation pin asserts "m" → "Monthly", so it WOULD
    //     fail under that regression. Wait — let me re-check: the existing
    //     pin asserts the lowercase input produces the Monthly label. If
    //     the M arm is dropped, "m" → ToUpperInvariant → "M" → default
    //     (echo "M") — the existing pin fails. So the M arm drop IS
    //     caught by the existing pin indirectly.
    //   • What the existing pin CAN'T see: a refactor that adds a
    //     case-sensitive equality check ahead of the existing switch,
    //     e.g. `if (frequency == "m") return "Monthly";` then drops the
    //     `M` arm. The lowercase pin passes (early-return fires), but
    //     the canonical uppercase wire form from FRED ("M") falls
    //     through to default and renders raw "M" on the dashboard.
    //
    //   The risk this pin UNIQUELY catches: a refactor that fragments
    //   the "M and m both → Monthly" contract by special-casing one
    //   variant. The pair (lowercase + uppercase) is the only way to
    //   detect that fragmentation; either alone passes.
    //
    // Monthly is FRED's MOST COMMON publication cadence — every CPI,
    // PCE, retail-sales, housing-starts, employment-situation,
    // capacity-utilisation, industrial-production series ships monthly.
    // The label appears on virtually every series listing.
    //
    // Pin "M" (uppercase, FRED's canonical wire form) and assert the
    // exact "Monthly" literal. After this PR, the full ExpandFrequency
    // switch (D, W, BW, M, Q, SA, A, lowercase, Unknown) is exhaustively
    // pinned arm-by-arm.
    [Fact]
    public void ExpandFrequency_MonthlyCodeM_ReturnsMonthlyViaExplicitUppercaseArm()
    {
        var method = typeof(EconomicDataController).GetMethod(
            "ExpandFrequency",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, ["M"]);

        result.Should().Be("Monthly");
    }
}
