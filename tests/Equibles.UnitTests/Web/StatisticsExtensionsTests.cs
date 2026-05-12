using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class StatisticsExtensionsTests {
    [Fact]
    public void SafeRound_NaN_ReturnsNullInsteadOfThrowingOnDecimalCast() {
        // MathNet.Numerics.Statistics.DescriptiveStatistics.StandardDeviation returns
        // double.NaN for any single-value sample — a realistic shape coming out of
        // TechnicalIndicatorService when an instrument has only one closing price in
        // the requested window (e.g. an IPO's first trading day, or a thinly-traded
        // ticker over a short range). `SafeRound` exists specifically to protect the
        // downstream `(decimal)` cast: casting `double.NaN` to `decimal` throws
        // OverflowException at runtime, which would crash the view render and serve
        // a 500 instead of a graceful empty-cell. The guard is a single
        // `double.IsFinite(value)` check — drop it and every single-row indicator
        // calculation becomes a runtime crash that escapes the test suite (since the
        // existing TechnicalIndicatorService tests all use multi-point inputs).
        // Pin the NaN path so a "simplify" refactor that removes IsFinite is caught.
        var result = double.NaN.SafeRound(2);

        result.Should().BeNull();
    }
}
