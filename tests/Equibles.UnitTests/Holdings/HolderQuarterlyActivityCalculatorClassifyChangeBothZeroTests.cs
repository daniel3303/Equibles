using System.Reflection;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderQuarterlyActivityCalculatorClassifyChangeBothZeroTests
{
    // The five per-arm pins cover each classification in isolation, but not the
    // precedence at the intersection of the first two guards. Both the
    // previous-is-0 (Initiated) and current-is-0 (Exited) arms match when a
    // representable 0-share-both-quarters row arrives; the `previousShares == 0`
    // guard is evaluated FIRST, so the contract a caller relies on is Initiated.
    // Reordering the guards (checking current==0 first) would silently flip this
    // degenerate row to Exited — this pins the ordering.
    [Fact]
    public void ClassifyChange_BothCurrentAndPriorZero_PrefersInitiatedOverExited()
    {
        var method = typeof(HolderQuarterlyActivityCalculator).GetMethod(
            "ClassifyChange",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (StockPositionChangeType)method!.Invoke(null, [0L, 0L]);

        result.Should().Be(StockPositionChangeType.Initiated);
    }
}
