using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class EconomicDataControllerExpandFrequencyWeeklyTests
{
    // ExpandFrequency per-arm sweep continues. Pinned so far:
    //   BW (Biweekly), Q (Quarterly), SA (Semiannual), A (Annual),
    //   D (Daily), lowercase-normalisation (Monthly via "m"), Unknown.
    // This pin covers W → Weekly. After this iteration, only M (Monthly
    // direct) remains for exhaustive per-arm coverage.
    //
    // W is the SECOND case in source order (right after D). FRED
    // publishes a substantial weekly series catalog: initial jobless
    // claims, H.4.1 Federal Reserve balance-sheet snapshots, weekly
    // M2/M3 monetary aggregates, weekly oil/gas inventories. The
    // dashboard's series-frequency chip renders the human label for
    // every series listing.
    //
    // The risk this pin uniquely catches and that the other arm pins
    // cannot:
    //   • SWAP regression — `"W" => "Biweekly"` (copy-paste from BW
    //     two lines below) — would compile, pass BW (different arm),
    //     pass D (different arm), and silently mis-label every weekly
    //     FRED series as biweekly. The two cadences are economically
    //     adjacent but analytically different — biweekly jobless
    //     claims would be a meaningless concept.
    //   • DROP regression — `case "W":` removed → falls to default,
    //     renders raw "W" code in the frequency chip.
    //   • CASE-WIDTH regression — `case "WK":` would compile, pass
    //     all sibling pins (each in its own case), and silently
    //     misroute every "W" wire code to default.
    //
    // Pin "W" (uppercase, FRED's canonical wire form) and assert the
    // exact "Weekly" literal. Reflection-invoke since private static.
    [Fact]
    public void ExpandFrequency_WeeklyCodeW_ReturnsWeekly()
    {
        var method = typeof(EconomicDataController).GetMethod(
            "ExpandFrequency",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, ["W"]);

        result.Should().Be("Weekly");
    }
}
