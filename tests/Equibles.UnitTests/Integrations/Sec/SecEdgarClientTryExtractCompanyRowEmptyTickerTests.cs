using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientTryExtractCompanyRowEmptyTickerTests
{
    // Fourth and FINAL pin in the TryExtractCompanyRow family. Closes the
    // OR guard by covering the empty-TICKER half — the empty-CIK half was
    // pinned in PR #2305. After this PR, every meaningful arm of the
    // helper is individually defended:
    //   • Null-name tolerance (existing) — name=null OK with valid cik+ticker
    //   • Short-row boundary guard (#2304) — row too short → false
    //   • Empty-CIK rejection (#2305) — first half of the OR
    //   • Empty-TICKER rejection (this pin) — second half of the OR
    //
    // The contract on this arm:
    //   if (string.IsNullOrEmpty(cik) || string.IsNullOrEmpty(ticker))
    //       return false;
    //                          ^ this branch
    //
    // SEC's company_tickers.json occasionally serves rows with an empty
    // ticker — delisted issuers whose ticker was reclaimed by another
    // company but whose CIK row remains in the feed for historical
    // continuity. Without the empty-ticker check, the downstream
    // CompanySyncService would insert a CommonStock with `Ticker = ""`,
    // colliding with every other empty-ticker entry on the
    // `unique(Ticker)` constraint and aborting the entire company
    // sync batch.
    //
    // The risk this pin uniquely catches and that the empty-CIK sibling
    // cannot:
    //   • SHORT-CIRCUIT ORDER regression — `IsNullOrEmpty(ticker) ||
    //     IsNullOrEmpty(cik)` (someone swaps the OR operands during
    //     a stylistic pass) — would compile, pass the empty-CIK sibling
    //     (empty CIK still short-circuits via the now-first ticker
    //     check that also evaluates first... wait, no — both arms still
    //     return false on any empty value because of the OR, and
    //     swapping operands doesn't change the result). So this swap
    //     is benign for the boolean result.
    //   • DROP-the-empty-ticker check — `if (IsNullOrEmpty(cik))
    //     return false;` (drops the ticker half) — would compile,
    //     pass the empty-CIK sibling (cik check still fires), and
    //     silently accept empty-ticker rows. The empty-CIK sibling
    //     CANNOT see this — its input has empty cik so the first
    //     check fires regardless of whether the ticker half exists.
    //     The pair (empty-cik + empty-ticker) is the only way to
    //     defend both halves of the OR.
    //
    // Pin: pass a row where TICKER is the empty string but CIK and
    // name are valid. Assert the helper returns false. The OR
    // short-circuits on the first true subexpression, so this pin
    // exercises the TICKER arm cleanly (CIK is valid → first arm
    // short-circuits to false → OR evaluates the ticker arm → true →
    // return false). Reflection-invoke since private static.
    [Fact]
    public void TryExtractCompanyRow_EmptyTickerWithValidCikAndName_ReturnsFalse()
    {
        var method = typeof(SecEdgarClient).GetMethod(
            "TryExtractCompanyRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // Row has all three fields, but ticker is empty.
        var row = new List<object> { "0000320193", "Apple Inc.", "" };
        var args = new object[] { row, 0, 1, 2, null, null, null };

        var result = (bool)method!.Invoke(null, args);

        result.Should().BeFalse();
    }
}
