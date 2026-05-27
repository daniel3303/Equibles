using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class CompanySyncServiceParseCikValidValueTests
{
    // Sibling to the existing ParseCik_UnparseableValue_ReturnsLongMaxValue
    // pin (CompanySyncServiceTests:293). That pin defends the FALLBACK arm
    // (long.TryParse fails → returns long.MaxValue so junk CIKs lose every
    // tiebreak). This pin covers the structurally distinct SUCCESS arm of
    // the same ternary:
    //   long.TryParse(cik, out var n) ? n : long.MaxValue
    //                                  ^ this branch
    //
    // The risk this pin uniquely catches and that the unparseable sibling
    // cannot: a refactor that returns `long.MaxValue` unconditionally —
    // e.g. someone "tidying" the ternary to `return long.MaxValue;` under
    // the (false) intuition that the helper is only used as a "lose-the-
    // tiebreak" sentinel — would compile, pass the existing
    // ParseCik_UnparseableValue pin (still returns MaxValue), and cause
    // EVERY CIK to lose every ticker-collision tiebreak. The downstream
    // cascade in ShouldIncumbentWin:
    //   return ParseCik(incumbent.Cik) <= ParseCik(incoming.Cik);
    // would degrade to `MaxValue <= MaxValue` = true, so every incumbent
    // wins regardless of the actual CIK numerical ordering, EXCEPT — wait,
    // that's actually a no-op for the production tiebreak semantic
    // ("smaller CIK wins, ties favour the incumbent"). The real downstream
    // damage is the inverse: the helper is also used in collision pathways
    // that rely on the ordering being MEANINGFUL — a "consolidate to one
    // value" regression makes every CIK equivalent for the tiebreak,
    // collapsing the deterministic resolution and reintroducing the
    // nondeterminism the ParseCik helper was extracted to prevent.
    //
    // The cleaner regression class this catches: a SWAP refactor
    //   long.TryParse(cik, out var n) ? long.MaxValue : n
    // (intuitive under "MaxValue is the default → use it for the success
    // arm too") would compile, pass the unparseable-MaxValue pin (junk
    // still routes to MaxValue), and INVERT every valid CIK's tiebreak
    // contribution. Valid incumbents would now lose every tie to
    // unparseable incoming CIKs (junk → MaxValue, valid → also MaxValue
    // via the swap), and the incumbent vs incoming comparison would
    // become arbitrary.
    //
    // Pin: invoke with a real-shaped SEC CIK ("320193" — Apple's CIK)
    // and assert the EXACT parsed long value (320193L). The dual
    // semantic — that the helper actually parses (vs returning a
    // sentinel) AND parses to the right value — distinguishes the
    // working success arm from both the always-MaxValue regression
    // and the swap regression.
    //
    // Reflection-invoke since the method is private static.
    [Fact]
    public void ParseCik_ValidNumericString_ReturnsParsedLongValue()
    {
        var method = typeof(CompanySyncService).GetMethod(
            "ParseCik",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (long)method!.Invoke(null, ["320193"]);

        result.Should().Be(320193L);
    }
}
