using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientTryExtractCompanyRowEmptyCikTests
{
    // Third pin in the TryExtractCompanyRow family:
    //   • Null-name tolerance (existing) — name may be null when cik+ticker valid
    //   • Short-row boundary guard (PR #2304) — row.Count <= maxIndex returns false
    //   • Empty-CIK rejection (this pin) — IsNullOrEmpty(cik) returns false
    //
    // The contract on the second guard:
    //   if (string.IsNullOrEmpty(cik) || string.IsNullOrEmpty(ticker))
    //       return false;
    // Real SEC company_tickers.json rows occasionally carry an empty CIK
    // (suspended registrants, post-merger placeholder entries) or empty
    // ticker (delisted issuers whose ticker was reclaimed). Both must
    // be rejected — the downstream CompanySyncService keys on the CIK
    // and ticker pair, and an empty CIK would silently create a
    // stock row with no SEC identity, breaking every future filing
    // lookup for that company.
    //
    // The risk this pin uniquely catches and that the sibling pins
    // cannot:
    //   • Drop-the-empty-cik check — `if (string.IsNullOrEmpty(ticker))
    //     return false;` (someone "tidies" the OR chain under the
    //     intuition that ticker is the user-facing identifier) — would
    //     compile, pass the null-name sibling (its CIK is "0000320193",
    //     non-empty), pass the short-row sibling (different guard
    //     entirely), and silently accept empty-CIK rows. The resulting
    //     CommonStock would be inserted with `Cik = ""` and every
    //     subsequent `GetByCik` lookup would either match the wrong
    //     row (if multiple empty-CIK rows exist) or fail to dedupe.
    //   • Drop-the-empty-ticker check — symmetric regression. The pair
    //     (this pin + a future empty-ticker sibling) catches both halves
    //     of the OR.
    //   • Swap to whitespace-tolerant guard — `IsNullOrWhiteSpace` —
    //     would compile and tighten the guard (rejects "   "); this pin
    //     still passes (its CIK is the empty string, also rejected by
    //     IsNullOrWhiteSpace). Benign refactor that doesn't fail.
    //
    // Pin: pass a row where CIK is the empty string but ticker and name
    // are valid. Assert the helper returns false. The dual ordering
    // — empty CIK comes first in the OR — means this pin exercises
    // ONLY the CIK arm of the guard (ticker check short-circuits).
    // Reflection-invoke since private static.
    [Fact]
    public void TryExtractCompanyRow_EmptyCikWithValidTickerAndName_ReturnsFalse()
    {
        var method = typeof(SecEdgarClient).GetMethod(
            "TryExtractCompanyRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // Row has all three fields, but CIK is empty.
        var row = new List<object> { "", "Apple Inc.", "AAPL" };
        var args = new object[] { row, 0, 1, 2, null, null, null };

        var result = (bool)method!.Invoke(null, args);

        result.Should().BeFalse();
    }
}
