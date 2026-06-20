using System.Reflection;
using Equibles.Finra.BusinessLogic;

namespace Equibles.UnitTests.Finra;

/// <summary>
/// Mirror of the tied-minimum percentile pin, attacking the opposite extreme: a tie
/// at the MAXIMUM must share the mean of the ranks it spans (not snap to 100), while
/// the distinct minimum maps to 0. Guards the average-rank loop's handling of a tie
/// that runs to the end of the ordered set — an off-by-one there would mis-score the
/// most-shorted names, which sit at the top of the squeeze board.
/// </summary>
public class ShortSqueezeScoreManagerPercentilesTiedMaximumTests
{
    [Fact]
    public void Percentiles_TiedMaximumSpanningTwoRanks_SharesMeanRankAndMinMapsTo0()
    {
        var min = Guid.NewGuid();
        var tiedA = Guid.NewGuid();
        var tiedB = Guid.NewGuid();
        var values = new List<(Guid Id, decimal Value)> { (tiedA, 20m), (min, 10m), (tiedB, 20m) };

        var result = InvokePercentiles(values);

        // Distinct minimum sits at rank 0 → 0. The two tied maxima share the mean of
        // 0-based ranks 1..2 = 1.5; 1.5 / (3 - 1) * 100 = 75 — never 100.
        result[min].Should().Be(0, "the distinct minimum maps to the bottom of the scale");
        result[tiedA].Should().BeApproximately(75, 1e-9);
        result[tiedB].Should().BeApproximately(75, 1e-9);
    }

    private static Dictionary<Guid, double> InvokePercentiles(
        IEnumerable<(Guid Id, decimal Value)> values
    )
    {
        var method = typeof(ShortSqueezeScoreManager).GetMethod(
            "Percentiles",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method.Should().NotBeNull("the percentile normalization helper should exist");
        return (Dictionary<Guid, double>)method!.Invoke(null, [values]);
    }
}
