using System.Reflection;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderQuarterlyActivityCalculatorClassifyChangeReducedTests
{
    // Sibling to HolderQuarterlyActivityCalculatorClassifyChangeInitiatedTests
    // (PR #2314). That pins arm 1 (previous=0 → Initiated). This pin covers
    // the structurally distinct DEFAULT/REDUCED arm — the false branch of
    // the closing ternary:
    //   return currentShares > previousShares
    //       ? StockPositionChangeType.Increased
    //       : StockPositionChangeType.Reduced;
    //                              ^ this branch
    //
    // The four arms of ClassifyChange and the regressions each pin defends:
    //   1. previous=0 → Initiated (PR #2314)
    //   2. current == previous → Unchanged (unpinned)
    //   3. current > previous → Increased (unpinned)
    //   4. current < previous → Reduced (this pin)
    //
    // The risk this pin uniquely catches:
    //   • SWAP regression — the closing ternary's two arms swapped:
    //     `return current > previous ? Reduced : Increased;` (logic flip
    //     from a careless edit) — would compile, pass the Initiated
    //     sibling (its input has previous=0 → arm 1 fires before the
    //     ternary), and INVERT every Increased/Reduced classification.
    //     The portfolio-activity view's "Increased" and "Reduced"
    //     sections would visibly swap stocks. Caught by THIS pin
    //     (its input has current=500 < previous=1000, expects
    //     Reduced; swap returns Increased, fails).
    //   • Drop-the-Reduced-default — `return current > previous ?
    //     Increased : Unchanged;` (someone "tidies" the default to
    //     Unchanged) — would compile, pass the Initiated sibling, pass
    //     any future Unchanged sibling for equal inputs, and
    //     mis-classify every actual reduction as Unchanged. Caught
    //     here (input has current != previous, expects Reduced;
    //     this regression returns Unchanged).
    //
    // Pin: invoke with current=500, previous=1000 (a clear share-count
    // reduction). Assert StockPositionChangeType.Reduced. Reflection-
    // invoke since private static. Pair (Initiated + Reduced) defends
    // both the FIRST and LAST arms of the classifier; Unchanged and
    // Increased remain as future sibling targets.
    [Fact]
    public void ClassifyChange_CurrentBelowPrevious_ReturnsReduced()
    {
        var method = typeof(HolderQuarterlyActivityCalculator).GetMethod(
            "ClassifyChange",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (StockPositionChangeType)method!.Invoke(null, [500L, 1000L]);

        result.Should().Be(StockPositionChangeType.Reduced);
    }
}
