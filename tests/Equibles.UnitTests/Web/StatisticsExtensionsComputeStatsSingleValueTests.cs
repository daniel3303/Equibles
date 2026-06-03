using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class StatisticsExtensionsComputeStatsSingleValueTests
{
    // Contract (SafeRound doc, StatisticsExtensions.cs:9-10): "StandardDeviation
    // returns NaN for a single-value sample" — SafeRound exists precisely to null
    // that out. So ComputeStats on a one-element sample must report the value for
    // Mean/Median/Min/Max and NULL for StdDev (undefined for n=1), never NaN or a
    // cast crash. The existing skewed-sample pin uses three elements; the n=1
    // degenerate edge the guard targets is end-to-end unexercised.
    [Fact]
    public void ComputeStats_SingleValueSample_StdDevIsNullAndOthersAreTheValue()
    {
        var summary = new[] { 42.0 }.ComputeStats(decimals: 2);

        summary.Mean.Should().Be(42m);
        summary.Median.Should().Be(42m);
        summary.Min.Should().Be(42m);
        summary.Max.Should().Be(42m);
        summary.StdDev.Should().BeNull();
    }
}
