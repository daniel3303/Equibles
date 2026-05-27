using System.Reflection;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderQuarterlyActivityCalculatorClassifyChangeInitiatedTests
{
    // ClassifyChange is the four-arm classifier that drives the "stocks the
    // manager initiated / unchanged / increased / reduced this quarter"
    // breakdown on the institution-portfolio quarterly-activity view. The
    // four arms are:
    //   1. previousShares == 0           → Initiated
    //   2. currentShares == previousShares → Unchanged
    //   3. currentShares > previousShares  → Increased
    //   4. else                            → Reduced
    //
    // The ORDER of arms 1 and 2 is load-bearing. Arm 1 fires first because
    // a position with previousShares == 0 is semantically "Initiated this
    // quarter" — the manager didn't hold this stock before. Arm 2 alone
    // would incorrectly classify a (current=1000, previous=0) position as
    // Unchanged would never fire there, but more critically the SUBSEQUENT
    // arms would mis-classify: with `previous=0` and `current=1000`,
    // `current > previous` is true, so a refactor that DROPPED arm 1 (or
    // re-ordered it after arm 3) would silently re-classify every newly-
    // initiated position as "Increased" — the analytics dashboard would
    // show no first-time positions ever, only "increased from zero" which
    // is conceptually the same outcome but visually buckets under the
    // wrong heading.
    //
    // The risk this pin uniquely catches:
    //   • Drop-the-Initiated-arm — `return current > previous ?
    //     Increased : Reduced;` (a "tidy three arms into two" cleanup)
    //     would compile, every other arm pin (no other arms are pinned)
    //     would still pass for unchanged/increased/reduced inputs, and
    //     silently merge "newly initiated" positions into the "increased"
    //     bucket. The portfolio-activity view's "New Positions" section
    //     would render as empty on every report.
    //   • Order-swap regression — moving arm 2 before arm 1 — would NOT
    //     change behavior for non-degenerate inputs (only the degenerate
    //     current=0/previous=0 case would route differently), so this
    //     regression is benign in practice. A pin on the Initiated arm
    //     surfaces only the drop-the-arm regression.
    //
    // Pin: invoke ClassifyChange(currentShares=1000L, previousShares=0L)
    // and assert StockPositionChangeType.Initiated. The 1000-share value
    // ensures `current > previous` would be true (catches drop-into-
    // Increased) and distinguishes from the degenerate 0/0 case. Reflection-
    // invoke since the helper is private static.
    [Fact]
    public void ClassifyChange_PreviousZeroCurrentPositive_ReturnsInitiated()
    {
        var method = typeof(HolderQuarterlyActivityCalculator).GetMethod(
            "ClassifyChange",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (StockPositionChangeType)method!.Invoke(null, [1000L, 0L]);

        result.Should().Be(StockPositionChangeType.Initiated);
    }
}
