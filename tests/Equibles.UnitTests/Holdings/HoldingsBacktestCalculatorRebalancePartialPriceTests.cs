using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorRebalancePartialPriceTests
{
    private static readonly Guid StockA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid StockB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Calculate_RebalanceWithOnePositionMissingPrice_PortfolioStartReflectsPricedAllocationOnly()
    {
        // Contract (Calculate XML doc): "if priceOf returns null for a held stock on a
        // given day, that position's contribution is excluded that day." For a two-
        // position snapshot where Stock A has no price on the rebalance day but B does,
        // the first PortfolioValue point is therefore weight(B)*InitialValue = 20 — not
        // the InitialValue=100 normalization that holds when every position is priced.
        // The existing DelistedStockPriceNull test only exercises the single-stock
        // null-fallback path; this fills in the multi-position partial-price branch.
        var rebalanceDate = new DateOnly(2024, 5, 15);
        var reportDate = rebalanceDate.AddDays(-HoldingsBacktestCalculator.RebalanceDelayDays);

        var snapshot = new BacktestQuarterSnapshot
        {
            ReportDate = reportDate,
            Positions =
            {
                new BacktestPosition
                {
                    CommonStockId = StockA,
                    Shares = 1_000,
                    Value = 80_000_000,
                    IsOption = false,
                },
                new BacktestPosition
                {
                    CommonStockId = StockB,
                    Shares = 200,
                    Value = 20_000_000,
                    IsOption = false,
                },
            },
        };

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate,
            priceOf: (stockId, _) => stockId == StockB ? 100m : (decimal?)null,
            benchmarkPriceOf: _ => 50m
        );

        result.Points.Should().ContainSingle();
        result
            .Points[0]
            .PortfolioValue.Should()
            .Be(
                20m,
                "20% of the portfolio (Stock B) is priced; the unpriced position's weight is dropped per the exclude-contribution contract"
            );
    }
}
