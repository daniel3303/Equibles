using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorMultiSnapshotMarkBeforeRebalanceTests
{
    private static readonly Guid StockA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid StockB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // The inner advance loop in Calculate is documented to mark-to-market with
    // the prior holdings BEFORE rebalancing — "so the rebalance uses an honest
    // portfolio value" (see HoldingsBacktestCalculator.cs near line 90). When a
    // held stock's price jumps on the same day the next snapshot's rebalance
    // date is reached, that gain must be captured into portfolioValue first,
    // and the new positions allocated against the inflated value. Swapping the
    // order would size the second rebalance against the stale prior-day value
    // and silently strip the cloner of any rebalance-day gain.
    [Fact]
    public void Calculate_PriceJumpsOnSecondRebalanceDay_NewPositionSizedAgainstMarkedValue()
    {
        var snapshotA = new BacktestQuarterSnapshot
        {
            ReportDate = new DateOnly(2024, 1, 1),
            Positions =
            {
                new BacktestPosition
                {
                    CommonStockId = StockA,
                    Shares = 10_000,
                    Value = 1_000_000,
                    IsOption = false,
                },
            },
        };
        var snapshotB = new BacktestQuarterSnapshot
        {
            ReportDate = new DateOnly(2024, 4, 1),
            Positions =
            {
                new BacktestPosition
                {
                    CommonStockId = StockB,
                    Shares = 10_000,
                    Value = 1_000_000,
                    IsOption = false,
                },
            },
        };
        var firstRebalance = snapshotA.ReportDate.AddDays(
            HoldingsBacktestCalculator.RebalanceDelayDays
        );
        var secondRebalance = snapshotB.ReportDate.AddDays(
            HoldingsBacktestCalculator.RebalanceDelayDays
        );

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshotA, snapshotB],
            from: firstRebalance,
            to: secondRebalance.AddDays(1),
            priceOf: (stockId, day) =>
            {
                if (stockId == StockA)
                    return day < secondRebalance ? 100m : 200m;
                return 100m;
            },
            benchmarkPriceOf: _ => 100m
        );

        // StockA doubles to $200 exactly on the second rebalance day. The
        // marked-then-rebalanced contract sizes the StockB position against
        // the $200 marked value, so the post-rebalance portfolio stays at
        // $200; an out-of-order rebalance would size against the stale $100
        // and the final point would be ~$100.
        result.Points.Last().PortfolioValue.Should().Be(200m);
    }
}
