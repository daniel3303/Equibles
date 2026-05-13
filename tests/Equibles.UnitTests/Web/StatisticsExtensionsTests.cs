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

    [Fact]
    public void SafeRound_PositiveInfinity_ReturnsNullInsteadOfThrowingOnDecimalCast() {
        // Sibling to the NaN pin above. The risk this catches is asymmetric and
        // unreachable from the NaN test alone: the guard is `double.IsFinite(value)`,
        // which returns FALSE for `NaN`, `PositiveInfinity`, AND `NegativeInfinity`.
        // A regression that "tightened" the guard to `!double.IsNaN(value)` — a
        // plausible "simplify" refactor by someone who only ever saw NaN in practice
        // — would let infinity values through to `(decimal)` cast, which throws
        // `OverflowException` exactly like the NaN cast does. The NaN test would
        // still pass; the infinity case would silently crash the view render.
        //
        // The realistic trigger is `1.0 / 0.0` in float arithmetic — common in
        // variance/divergence calculations over a zero-spread window (constant
        // prices), or in beta/correlation when the denominator covariance
        // collapses to zero. MathNet's `DescriptiveStatistics` and several
        // `TechnicalIndicatorService` helpers can produce infinity outputs on
        // degenerate inputs that pass through to the view-model decimal cast.
        //
        // The pair (NaN → null, +Inf → null) distinguishes a working IsFinite
        // guard from BOTH `!IsNaN` AND `>= 0` narrowings. Pinning +Inf
        // specifically is the sharper edge — `IsNaN` regression slips past the
        // existing test, and infinity values trigger the same overflow.
        var result = double.PositiveInfinity.SafeRound(2);

        result.Should().BeNull();
    }

    [Fact]
    public void ComputeSma_AlignsToInput_FirstWindowSlotsAreNullThenRoundedSma() {
        // ComputeSma is consumed by view models that overlay SMA series on price
        // charts; alignment to the original prices array matters because the
        // chart's x-axis is indexed by trading day. The first (period-1)
        // entries MUST be null so the SMA line starts only when a full window
        // has accumulated — otherwise the chart shows a misleading non-null
        // value at day 0 that's actually computed from incomplete data. A
        // refactor that drops the alignment (e.g. returns the raw
        // MovingAverage sequence without the null prefix) would silently
        // shift every SMA point one period to the left.
        var values = new[] { 1.0, 2.0, 3.0, 4.0, 5.0 };

        var result = values.ComputeSma(period: 3, digits: 2);

        result.Should().HaveCount(5);
        result[0].Should().BeNull();
        result[1].Should().BeNull();
        result[2].Should().Be(2.00m);
        result[3].Should().Be(3.00m);
        result[4].Should().Be(4.00m);
    }
}
