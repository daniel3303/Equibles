using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class EconomicDataControllerExpandFrequencySemiannualTests
{
    // Sibling pin in the ExpandFrequency family. Existing pins cover:
    //   • BW → Biweekly (BiweeklyCodeBw)
    //   • Q → Quarterly (QuarterlyCodeQ)
    //   • Lowercase → Monthly arm via ToUpperInvariant
    //   • A → Annual (AnnualCodeA)
    //   • Unknown → input unchanged (UnknownCode)
    // This pin covers the structurally distinct SA → Semiannual arm — the
    // only TWO-letter code in the switch that DOESN'T share BW's
    // "biweekly" prefix shape.
    //
    // Why SA is the highest-value of the remaining arms (D, W, M, SA) to
    // pin first:
    //   • SA is the SECOND multi-character case in the switch (after BW).
    //     The case label "SA" is exactly two characters; any refactor that
    //     "tidies" the multi-character cases into a regex or StartsWith
    //     comparison risks collapsing SA's mapping with another arm.
    //   • SA is uniquely susceptible to a "shorten the codes" refactor —
    //     someone observing that SA is just "S" + "A" might collapse it
    //     under the (false) intuition that semiannual is a sub-case of
    //     annual. The result: "SA" falls through to the default and
    //     renders as the raw "SA" string in FRED frequency labels (the
    //     Monetary-Base series, the H.4.1 Factors Affecting Reserve
    //     Balances, several CPI sub-indices all publish semiannually).
    //
    // The risk this pin uniquely catches and that the existing siblings
    // cannot:
    //   • SWAP regression — `"SA" => "Annual"` (intuitive copy-paste from
    //     the line below) would compile, pass BW/Q/A/Monthly/Unknown
    //     (each untouched), and silently MIS-LABEL every semiannual
    //     FRED series as annual in the economic-data dashboard. The two
    //     series families that publish semiannually (Monetary-Base and
    //     reserve-factor reports) would appear to update once a year
    //     instead of twice — a visible analytical regression.
    //   • DROP regression — `case "SA":` removed (collapses into default)
    //     would compile, pass every other arm sibling, and render the raw
    //     "SA" code as the frequency label. The unknown-code pin asserts
    //     that the default returns the input verbatim, so this regression
    //     PASSES unchanged-input → "SA"... wait, that's exactly what
    //     this pin's failure would look like. The pair (this pin
    //     asserting Semiannual + unknown asserting verbatim) is the only
    //     way to distinguish "SA arm working" from "SA fell to default
    //     and happens to return SA".
    //
    // Pin "SA" (uppercase, matching the switch label) and assert the
    // EXACT "Semiannual" literal. Reflection-invoke since private static.
    [Fact]
    public void ExpandFrequency_SemiannualCodeSa_ReturnsSemiannual()
    {
        var method = typeof(EconomicDataController).GetMethod(
            "ExpandFrequency",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, ["SA"]);

        result.Should().Be("Semiannual");
    }
}
