using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientTryExtractCompanyRowShortRowTests
{
    // Sibling to SecEdgarClientTryExtractCompanyRowNullNameToleranceTests
    // (null-name tolerance). This pin covers the structurally distinct
    // FIRST guard in TryExtractCompanyRow:
    //   if (row.Count <= Math.Max(Math.Max(cikIndex, nameIndex), tickerIndex))
    //       return false;
    //
    // The helper drives the parse of SEC EDGAR's company_tickers.json feed:
    // each row is a positional array of values, and the indices for CIK,
    // name, ticker come from the response's `fields` header. A short row
    // would trigger IndexOutOfRangeException downstream — every other
    // logical mishap (null name, empty CIK, empty ticker) returns null
    // via TryGetValue-style guards, so the boundary check is the unique
    // defence against ranged-index access.
    //
    // The risk this pin uniquely catches:
    //   • Drop-the-boundary regression — `if (row.Count == 0) return false;`
    //     (a "simplify the Math.Max chain" cleanup pass under the wrong
    //     intuition that only the empty case matters) — would compile,
    //     pass the null-name sibling pin (its row has 4 elements, all
    //     indices in range), and throw IndexOutOfRangeException on
    //     real SEC rows where the upstream company_tickers.json
    //     response omits the trailing exchange column (historical
    //     filings pre-2018, or a future schema variant). Each crash
    //     in `row[cikIndex]?.ToString()` aborts the entire company-
    //     sync batch.
    //   • Off-by-one regression — `row.Count < Math.Max(...)` (using
    //     `<` instead of `<=`) — would compile, pass the null-name
    //     sibling, and IndexOutOfRangeException when the row is exactly
    //     `maxIndex` long (the boundary). The `<=` semantic is
    //     load-bearing: indices are 0-based, so a row of length N can
    //     safely access indices 0..N-1; the guard requires N > maxIndex,
    //     i.e. `row.Count > Math.Max(...)`, equivalently `<=` returns
    //     false.
    //
    // Pin: pass a row with FEWER elements than the maximum index. Assert
    // (a) the helper returns false AND (b) no exception is thrown.
    // Reflection-invoke with explicit out-parameter argument slots; the
    // dual assertion distinguishes the working guard from both regression
    // classes.
    [Fact]
    public void TryExtractCompanyRow_RowShorterThanMaxIndex_ReturnsFalseWithoutThrowing()
    {
        var method = typeof(SecEdgarClient).GetMethod(
            "TryExtractCompanyRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // Row has only 2 elements; tickerIndex (3) is out of range.
        var row = new List<object> { "0000320193", "Apple Inc." };
        var args = new object[] { row, 0, 1, 3, null, null, null };

        var act = () => (bool)method!.Invoke(null, args);

        var result = act.Should().NotThrow().Subject;
        result.Should().BeFalse();
    }
}
