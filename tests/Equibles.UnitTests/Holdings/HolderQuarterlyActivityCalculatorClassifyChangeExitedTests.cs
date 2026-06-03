using System.Reflection;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderQuarterlyActivityCalculatorClassifyChangeExitedTests
{
    // The four sibling ClassifyChange pins (Initiated/Unchanged/Increased/Reduced)
    // miss arm 2: a position still PRESENT in the current quarter but reporting 0
    // shares, with prior shares > 0, is Exited. The bucket-level Exited tests cover
    // the absent-from-current path (the second loop / BuildExitedChange), not this
    // branch. Drop `if (currentShares == 0) return Exited;` and a sold-to-zero row
    // falls through to 0 < previous => Reduced — mis-bucketing a full exit.
    [Fact]
    public void ClassifyChange_CurrentZeroWithPriorShares_ReturnsExited()
    {
        var method = typeof(HolderQuarterlyActivityCalculator).GetMethod(
            "ClassifyChange",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (StockPositionChangeType)method!.Invoke(null, [0L, 1000L]);

        result.Should().Be(StockPositionChangeType.Exited);
    }
}
