using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Contract: Calculate accepts any valid DateOnly from/to pair and returns a
/// BacktestResult — invalid inputs yield a result with a Reason string, never
/// an unhandled exception. The horizon clamp <c>from.AddYears(MaxYears)</c>
/// overflows when from.Year > 9999 - MaxYears because DateOnly cannot
/// represent year 10000+. Passing a valid date near DateOnly.MaxValue should
/// gracefully clamp rather than throw ArgumentOutOfRangeException.
/// </summary>
public class HoldingsBacktestCalculatorNearMaxDateOverflowTests
{
    private static readonly Guid StockA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Calculate_FromNearDateOnlyMaxValue_DoesNotThrow()
    {
        var from = new DateOnly(9995, 1, 1);
        var to = new DateOnly(9999, 12, 31);
        var snapshot = new BacktestQuarterSnapshot
        {
            ReportDate = new DateOnly(9994, 9, 30),
            Positions =
            [
                new BacktestPosition
                {
                    CommonStockId = StockA,
                    Shares = 1000,
                    Value = 100_000,
                    IsOption = false,
                },
            ],
        };

        var act = () =>
            HoldingsBacktestCalculator.Calculate([snapshot], from, to, (_, _) => 100m, _ => 100m);

        act.Should().NotThrow("Calculate should handle dates near DateOnly.MaxValue gracefully");
    }
}
