using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class InstitutionPortfolioSummaryCalculatorTurnoverDecreasedPositionTests
{
    // ComputeQoQTurnoverPercent (extracted in #1612) uses Math.Abs on the
    // share delta so a decreased position contributes the same dollar
    // movement as an equally-sized increase. The existing increased-position
    // test only covers positive deltas; a refactor that dropped Math.Abs
    // (or replaced |delta| with `current - prior`) would yield a negative
    // turnover number whenever the holder reduced a position, which the
    // existing matrix wouldn't catch.
    [Fact]
    public void Calculate_TurnoverWithDecreasedPosition_AbsValueProducesPositivePercent()
    {
        var stockId = Guid.NewGuid();
        // Current quarter: 500 shares @ $500_000 → $1_000 per share proxy.
        var current = MakeHolding(stockId, shares: 500, value: 500_000);
        // Prior quarter: 1_000 shares (only Shares is read on the prior side).
        var previous = MakeHolding(stockId, shares: 1_000, value: 1_000_000);

        var result = InstitutionPortfolioSummaryCalculator.Calculate(
            [current],
            [previous],
            quartersReported: 2,
            latestReportDate: new DateOnly(2024, 12, 31),
            previousReportDate: new DateOnly(2024, 9, 30)
        );

        // |Δ shares| = |500 - 1000| = 500. proxy = 500_000 / 500 = $1_000.
        // turnover dollars = 500 × $1_000 = $500_000.
        // % = 500_000 / (2 × 500_000) = 50%.
        result.QoQTurnoverPercent.Should().BeApproximately(50.0, precision: 0.01);
    }

    private static InstitutionalHolding MakeHolding(Guid stockId, long shares, long value) =>
        new()
        {
            CommonStockId = stockId,
            Shares = shares,
            Value = value,
        };
}
