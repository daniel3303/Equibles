using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorRebalanceDateOfOverflowClampTests
{
    [Fact]
    public void RebalanceDateOf_ReportDateOverflowsCalendar_ClampsToMaxValue()
    {
        // Contract (doc-comment): a ReportDate within RebalanceDelayDays of DateOnly.MaxValue
        // would push +45 days past the calendar, so the shift is capped at MaxValue instead of
        // throwing. The two existing overflow tests only assert Calculate "does not throw"; none
        // pins the clamp VALUE on the public member. A ReportDate of MaxValue itself overflows
        // unconditionally, so the oracle is exactly DateOnly.MaxValue.
        var result = HoldingsBacktestCalculator.RebalanceDateOf(DateOnly.MaxValue);

        result.Should().Be(DateOnly.MaxValue);
    }
}
