using System.Reflection;
using Equibles.Web.Controllers;
using Equibles.Web.ViewModels.Stocks;

namespace Equibles.UnitTests.Web;

public class StocksControllerParsePositionTypesAllInvalidTests
{
    // Third pin in the ParsePositionTypes family. The existing siblings
    // pin the mixed-valid-and-invalid case (kept valid) and the
    // numeric-undefined case (rejected). This pin covers the
    // structurally distinct EMPTY-RESULT arm of the closing ternary:
    //   return result.Count > 0 ? result : null;
    //                                       ^ this branch
    //
    // The contract: when EVERY token in the input is invalid (so the
    // HashSet ends empty), return null. Downstream filter code uses
    // `null` as the sentinel for "no filter" — a non-null empty
    // HashSet would be misinterpreted as "filter to MATCH NONE", which
    // would silently hide every result from the holdings activity
    // dashboard.
    //
    // The risk this pin uniquely catches:
    //   • Drop-the-ternary refactor — `return result;` (a "tidy up the
    //     terminal ternary" cleanup) would return the empty HashSet
    //     instead of null. The downstream `if (filterTypes != null)`
    //     check (in HoldingsActivityController and elsewhere) would
    //     then enter the filter branch with an empty allow-list,
    //     filter every row out, and render an empty results page on
    //     every query whose filter string was all-invalid (typo, stale
    //     bookmark with old enum names, manual URL editing).
    //   • Inversion regression — `result.Count > 0 ? null : result`
    //     (logic flip) would return the populated HashSet as null and
    //     vice-versa. Caught by the mixed-valid-invalid sibling for
    //     the populated case AND here for the empty case.
    //
    // The mixed-valid sibling can't catch this because its input has
    // at least one valid token — `result.Count > 0` is true, the empty-
    // result branch never fires. Only an all-invalid input drives the
    // false arm of the terminal ternary.
    //
    // Reflection-invoke since ParsePositionTypes is private static.
    // Returns null (not empty HashSet) on all-invalid input.
    [Fact]
    public void ParsePositionTypes_AllTokensInvalid_ReturnsNullNotEmptyHashSet()
    {
        var method = typeof(StocksController).GetMethod(
            "ParsePositionTypes",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (HashSet<PositionChangeType>)method!.Invoke(null, ["xyz,abc,nope"]);

        result.Should().BeNull();
    }
}
