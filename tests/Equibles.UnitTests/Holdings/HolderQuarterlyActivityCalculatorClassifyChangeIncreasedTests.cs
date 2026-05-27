using System.Reflection;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderQuarterlyActivityCalculatorClassifyChangeIncreasedTests
{
    // Third pin in the ClassifyChange family. Initiated (PR #2314) and
    // Reduced (PR #2315) pin arms 1 and 4. This pin covers arm 3:
    //   currentShares > previousShares → StockPositionChangeType.Increased
    //
    // This is the TRUE branch of the closing ternary:
    //   return currentShares > previousShares
    //       ? StockPositionChangeType.Increased
    //       : StockPositionChangeType.Reduced;
    //
    // The Initiated arm (PR #2314) fires when previousShares == 0 BEFORE
    // the ternary. The Reduced arm (PR #2315) fires when current < previous.
    // Neither sibling can exclusively prove the Increased branch returns
    // the correct value — the swap regression `? Reduced : Increased`
    // fails the Reduced sibling (its current=500 < previous=1000 input
    // would return Increased instead of Reduced), but a DIFFERENT
    // regression that only touches the Increased branch (e.g.
    // `? Unchanged : Reduced`) would pass the Reduced sibling (Reduced
    // is unchanged) and silently mis-classify every increase as Unchanged.
    //
    // The risk this pin uniquely catches:
    //   • Drop-the-Increased-arm — `? Unchanged : Reduced` — would
    //     compile, pass Initiated (arm 1 fires first), pass Reduced
    //     (false-arm unchanged), and mis-classify every share-count
    //     increase as Unchanged in the portfolio-activity view.
    //     "Increased" section renders as empty; every actual increase
    //     hides under "Unchanged".
    //   • Asymmetric swap — `? Initiated : Reduced` — same issue;
    //     caught here.
    //
    // Pin: invoke with current=1500, previous=1000 (clear share-count
    // increase, previous nonzero so arm 1 doesn't fire). Assert
    // StockPositionChangeType.Increased. Reflection-invoke since
    // private static. The triad (Initiated + Reduced + Increased)
    // defends three of four arms — Unchanged remains as the final
    // sibling target.
    [Fact]
    public void ClassifyChange_CurrentAbovePrevious_ReturnsIncreased()
    {
        var method = typeof(HolderQuarterlyActivityCalculator).GetMethod(
            "ClassifyChange",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (StockPositionChangeType)method!.Invoke(null, [1500L, 1000L]);

        result.Should().Be(StockPositionChangeType.Increased);
    }
}
