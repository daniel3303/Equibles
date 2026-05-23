using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Contract: Calculate must never throw for valid DateOnly inputs. The rebalance
/// date is computed as ReportDate.AddDays(45); when the snapshot's ReportDate
/// falls in November or December of year 9999, AddDays(45) overflows past
/// DateOnly.MaxValue (9999-12-31). The existing near-max-date test uses
/// ReportDate = 9994-09-30 (+45 = 9994-11-14, safe) and misses this path.
/// </summary>
public class HoldingsBacktestCalculatorRebalanceDateOverflowTests
{
    private static readonly Guid StockA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Calculate_SnapshotReportDateNearMaxValue_DoesNotThrow()
    {
        // ReportDate 9999-11-20 + 45 days = 10000-01-04, which exceeds
        // DateOnly.MaxValue and throws ArgumentOutOfRangeException inside
        // the LINQ Select that computes RebalanceDate.
        var from = new DateOnly(9999, 1, 1);
        var to = DateOnly.MaxValue;
        var snapshot = new BacktestQuarterSnapshot
        {
            ReportDate = new DateOnly(9999, 11, 20),
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

        act.Should()
            .NotThrow(
                "ReportDate.AddDays(RebalanceDelayDays) near DateOnly.MaxValue must be clamped, not overflow"
            );
    }
}
