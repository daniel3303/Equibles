using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class EconomicDataControllerExpandFrequencyDailyTests
{
    // Continues the ExpandFrequency per-arm sweep. Existing pins cover:
    //   • BW → Biweekly
    //   • Q → Quarterly
    //   • SA → Semiannual (PR #2285)
    //   • A → Annual
    //   • Lowercase normalisation (Monthly arm)
    //   • Unknown → input echoed
    // This pin covers D → Daily — the highest-frequency arm and the most
    // common FRED series classification (daily Treasury yields, FX rates,
    // SOFR/EFFR, equity index closes, etc.).
    //
    // Why D is the right next pin: D is the FIRST case in the switch
    // expression source order. A "drop the top case" copy-paste pruning
    // regression (someone deletes the case they think is duplicated
    // with the lowercase normalisation pin) would compile, pass every
    // other arm sibling (each in its own case), and silently route
    // every daily FRED series to the default — rendering the raw
    // "D" code on the economic-data dashboard's series-frequency
    // chip instead of the human "Daily" label.
    //
    // Daily series are the highest-volume slice of the FRED universe
    // (~30% of FRED's catalog by series count). A label regression
    // there has the most visible per-render impact on the dashboard.
    //
    // The risk this pin uniquely catches and that the existing siblings
    // cannot:
    //   • SWAP regression — `"D" => "Weekly"` (copy-paste from the line
    //     below) — would compile, pass BW (different case), pass Q
    //     (different case), pass SA / A / Monthly / Unknown (different
    //     cases), and silently mis-label every Treasury yield curve
    //     series, every FX series, and every equity-index series on
    //     the economic-data dashboard as "Weekly".
    //   • DROP regression — `case "D":` removed → falls to default,
    //     renders raw "D" code in the frequency chip.
    //   • CASE-WIDTH regression — `case "DAY":` (someone "fixes" the
    //     code to match the full word) would compile, the lowercase-
    //     normalisation pin would still pass (its input is "m" → "M"
    //     → Monthly arm), but FRED's actual wire code "D" would fall
    //     through to default.
    //
    // Pin "D" (uppercase, the canonical FRED wire form) and assert the
    // EXACT "Daily" literal. Reflection-invoke since private static.
    //
    // After this iteration, six of the seven explicit arms (BW, Q, SA,
    // A, lowercase, D) plus the Unknown default are pinned; W and M
    // remain as future targets to complete exhaustive per-arm coverage.
    [Fact]
    public void ExpandFrequency_DailyCodeD_ReturnsDaily()
    {
        var method = typeof(EconomicDataController).GetMethod(
            "ExpandFrequency",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, ["D"]);

        result.Should().Be("Daily");
    }
}
