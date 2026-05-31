using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class StatisticsExtensionsComputeSmaNonFiniteWindowTests
{
    // Contract: ComputeSma returns a List<decimal?> that overlays an SMA series on a
    // price chart, one slot per input position. Its sibling SafeRound in the SAME file
    // exists specifically so a non-finite double (NaN / ±Infinity) — or a finite value
    // outside decimal's range — becomes null instead of throwing OverflowException on
    // the (decimal) cast. ComputeSma must honour that same guarantee for any FULL-window
    // position (index >= period-1): a window whose average is non-finite should yield
    // null, never throw. Here [1, 2, NaN] with period 2 makes the trailing window at
    // index 2 average (2 + NaN) -> NaN, a full-window slot the null-prefill does NOT
    // cover, so a correct implementation returns [null, 1.5, null]. The raw
    // (decimal?)Math.Round(NaN, ...) cast throws OverflowException instead. The existing
    // ComputeSma pins only feed finite, in-range inputs, so this unprotected region
    // (which the ComputeSmaPeriodLargerThanInput comment itself flags as a hazard)
    // escapes them.
    [Fact(
        Skip = "GH-2922 — ComputeSma throws OverflowException on a non-finite window instead of yielding null like SafeRound"
    )]
    public void ComputeSma_FullWindowAveragesToNonFinite_ReturnsNullInsteadOfThrowing()
    {
        double[] values = [1.0, 2.0, double.NaN];
        List<decimal?> result = [];

        var act = () => result = values.ComputeSma(period: 2, digits: 2);

        act.Should()
            .NotThrow(
                "a non-finite moving-average window must yield null like SafeRound, not throw OverflowException"
            );
        result.Should().Equal(new decimal?[] { null, 1.5m, null });
    }
}
