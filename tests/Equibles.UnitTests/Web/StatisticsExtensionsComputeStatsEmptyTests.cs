using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class StatisticsExtensionsComputeStatsEmptyTests
{
    // An empty sample has no defined mean/median/min/max/stddev. The SafeRound guard
    // exists to null out the non-finite results MathNet emits for degenerate samples,
    // so ComputeStats over no data must yield an all-null summary — never throw on the
    // decimal cast and never surface a NaN. The single-value edge is pinned elsewhere;
    // the zero-element edge (where Min/Max/Median can also be undefined) is not.
    [Fact]
    public void ComputeStats_EmptySample_AllStatisticsNullWithoutThrowing()
    {
        var summary = Array.Empty<double>().ComputeStats(decimals: 2);

        summary.Mean.Should().BeNull();
        summary.Median.Should().BeNull();
        summary.Min.Should().BeNull();
        summary.Max.Should().BeNull();
        summary.StdDev.Should().BeNull();
    }
}
