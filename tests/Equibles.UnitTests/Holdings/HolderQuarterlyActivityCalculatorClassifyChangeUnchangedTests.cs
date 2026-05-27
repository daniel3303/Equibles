using System.Reflection;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderQuarterlyActivityCalculatorClassifyChangeUnchangedTests
{
    // Final pin in the ClassifyChange per-arm sweep. After this PR every
    // arm of the four-arm classifier is individually defended:
    //   • Initiated (PR #2314)  — previousShares == 0
    //   • Reduced   (PR #2315)  — currentShares < previousShares
    //   • Increased (PR #2316)  — currentShares > previousShares
    //   • Unchanged (this PR)   — currentShares == previousShares
    //
    // The Unchanged arm fires when shares are equal and previous != 0
    // (arm 1 short-circuits the previous==0 case). Position-unchanged
    // is the most common quarter-over-quarter outcome — institutional
    // managers don't trade most of their portfolio every quarter.
    //
    // The risk this pin uniquely catches:
    //   • Drop-the-Unchanged-arm — `if (currentShares == previousShares
    //     ...) return Unchanged;` removed under "the ternary at the
    //     bottom handles equality implicitly via the > / < comparison"
    //     intuition. With the arm dropped, equal shares would fall
    //     through to `current > previous` (false) and the ternary
    //     returns Reduced. Every unchanged position would re-classify
    //     as "Reduced" — the dashboard would show every long-term
    //     holder as actively SELLING every quarter, a wildly misleading
    //     signal for the "manager activity" widget.
    //   • Swap-with-arm-1 regression — moving the equality check
    //     BEFORE the previous==0 check would route (current=0,
    //     previous=0) to Unchanged instead of Initiated. That's
    //     degenerate and arguably fine, but breaks the documented
    //     intent. Less critical than the drop regression.
    //
    // Pin: invoke with current=1000, previous=1000 (clear unchanged,
    // previous nonzero so arm 1 doesn't fire). Assert
    // StockPositionChangeType.Unchanged. Reflection-invoke since
    // private static. The quartet (Initiated + Reduced + Increased +
    // Unchanged) closes the per-arm sweep — any single arm dropped or
    // swapped surfaces at its corresponding sibling.
    [Fact]
    public void ClassifyChange_CurrentEqualsPreviousAndNonzero_ReturnsUnchanged()
    {
        var method = typeof(HolderQuarterlyActivityCalculator).GetMethod(
            "ClassifyChange",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (StockPositionChangeType)method!.Invoke(null, [1000L, 1000L]);

        result.Should().Be(StockPositionChangeType.Unchanged);
    }
}
