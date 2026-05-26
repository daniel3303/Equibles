using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class StatisticsExtensionsComputeSmaPeriodLargerThanInputTests
{
    // Contract: ComputeSma is "aligned to the input" and "positions before the
    // first full window are null". When the window is larger than the input
    // there is no full window — every position must therefore be null, and the
    // returned list length must still equal values.Length so the chart's
    // x-axis stays in lockstep with the price series. The realistic trigger is
    // a freshly-listed ticker with fewer than 20 (or 50, 200) trading days
    // feeding MarketController.ComputeSma(20, 2) / StockTabService's Sma200 —
    // a length mismatch or a crash here serves a 500 on the stock page.
    //
    // Sharpens the existing aligns-to-input test (which uses period=3 over 5
    // values, so the period-1 boundary fires mid-array). This pin fires the
    // boundary at i == values.Length-1 (i.e. NEVER) and asserts the all-null
    // result preserves length — catches a regression that drops the
    // null-prefill short-circuit and lets MathNet's NaN window outputs reach
    // the (decimal?)Math.Round cast (NaN → OverflowException).
    [Fact]
    public void ComputeSma_PeriodLargerThanValuesLength_ReturnsAllNullsAlignedToInput()
    {
        var values = new[] { 1.0, 2.0, 3.0 };

        var result = values.ComputeSma(period: 5, digits: 2);

        result.Should().HaveCount(3);
        result.Should().AllSatisfy(v => v.Should().BeNull());
    }
}
