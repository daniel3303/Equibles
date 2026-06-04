using Equibles.Yahoo.Repositories;

namespace Equibles.UnitTests.Yahoo;

public class PriceReturnCalculatorZeroBaseTests
{
    [Fact]
    public void Compute_TrailingWindowBaseCloseZero_ReturnsNullNotDivideByZero()
    {
        // Contract: a non-positive base close can't yield a meaningful percentage, so the
        // window returns null. Six bars is ENOUGH for the 5-day window (base is 5 bars back =
        // closes[0]), so a null here is the zero-base guard — not insufficient history — and
        // proves decimal division by zero (which throws) never runs.
        var dates = new List<DateOnly>
        {
            new(2024, 3, 11),
            new(2024, 3, 12),
            new(2024, 3, 13),
            new(2024, 3, 14),
            new(2024, 3, 15),
            new(2024, 3, 16),
        };
        var closes = new List<decimal> { 0m, 100m, 100m, 100m, 100m, 110m };

        var result = PriceReturnCalculator.Compute(dates, closes);

        result.FiveDay.Should().BeNull();
    }
}
