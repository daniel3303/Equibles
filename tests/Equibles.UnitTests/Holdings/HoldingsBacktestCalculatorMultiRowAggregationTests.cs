using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorMultiRowAggregationTests
{
    private static readonly Guid StockA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid StockB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // Rebalance's WHY-comment documents: "a holder may report a single stock
    // across multiple rows when multiple managers share discretion" — those
    // rows must be summed per CommonStockId before weights are computed.
    // A regression that overwrites `holdings[stockId]` per row (instead of
    // GroupBy + Sum) would silently misallocate the portfolio: with Stock A
    // split across two rows ($300k + $700k) and Stock B in one row ($1M),
    // the broken path treats the rows as separate and yields a +20% return
    // (under-invested in A), while the documented contract yields +50%
    // (A at the rebalance is 50% of the portfolio and it doubles).
    [Fact]
    public void Calculate_StockReportedAcrossTwoRows_AggregatesValueBeforeWeighting()
    {
        var snapshot = new BacktestQuarterSnapshot
        {
            ReportDate = new DateOnly(2024, 3, 31),
            Positions =
            {
                new BacktestPosition
                {
                    CommonStockId = StockA,
                    Shares = 3_000,
                    Value = 300_000,
                    IsOption = false,
                },
                new BacktestPosition
                {
                    CommonStockId = StockA,
                    Shares = 7_000,
                    Value = 700_000,
                    IsOption = false,
                },
                new BacktestPosition
                {
                    CommonStockId = StockB,
                    Shares = 10_000,
                    Value = 1_000_000,
                    IsOption = false,
                },
            },
        };
        var rebalanceDate = new DateOnly(2024, 5, 15);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(30),
            priceOf: (stockId, day) =>
            {
                if (stockId == StockA)
                    return day == rebalanceDate ? 100m : 200m;
                return 100m;
            },
            benchmarkPriceOf: _ => 100m
        );

        result.PortfolioSummary.TotalReturnPercent.Should().Be(50m);
    }
}
