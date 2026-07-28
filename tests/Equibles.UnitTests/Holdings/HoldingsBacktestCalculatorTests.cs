using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorTests
{
    private static readonly Guid StockA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StockB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Calculate_FromAfterTo_ReturnsReasonString()
    {
        var result = HoldingsBacktestCalculator.Calculate(
            [SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000)],
            new DateOnly(2024, 12, 31),
            new DateOnly(2024, 1, 1),
            (_, _) => 100m,
            _ => 100m
        );

        result.Points.Should().BeEmpty();
        result.Reason.Should().Contain("from must be on or before to");
    }

    [Fact]
    public void Calculate_EmptySnapshots_ReturnsReasonString()
    {
        var result = HoldingsBacktestCalculator.Calculate(
            [],
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            (_, _) => 100m,
            _ => 100m
        );

        result.Points.Should().BeEmpty();
        result.Reason.Should().Contain("no quarterly snapshots");
    }

    [Fact]
    public void Calculate_NoBenchmarkPrice_ReturnsReasonString()
    {
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            new DateOnly(2024, 5, 15),
            new DateOnly(2024, 5, 20),
            (_, _) => 100m,
            _ => null
        );

        result.Reason.Should().Contain("no benchmark price");
        result.Points.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_ZeroBenchmarkPrice_ReturnsReasonString()
    {
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            new DateOnly(2024, 5, 15),
            new DateOnly(2024, 5, 20),
            (_, _) => 100m,
            _ => 0m
        );

        result.Reason.Should().Contain("no benchmark price");
        result.Points.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_HorizonExceedsMaxYears_ClampsEndDate()
    {
        // The filer keeps filing right through the requested 25 years, so nothing goes stale and
        // the only thing bounding the simulation is the MaxYears horizon. Seeding the full history
        // matters: with a single snapshot the stale-tail cut would bind first and this would stop
        // testing the horizon clamp at all.
        var from = new DateOnly(2024, 5, 15);
        var snapshots = new List<BacktestQuarterSnapshot>();
        for (var quarter = 0; quarter < 100; quarter++)
        {
            snapshots.Add(
                SingleStockSnapshot(
                    new DateOnly(2024, 3, 31).AddMonths(quarter * 3),
                    StockA,
                    1_000_000
                )
            );
        }

        var result = HoldingsBacktestCalculator.Calculate(
            snapshots,
            from: from,
            to: from.AddYears(25),
            priceOf: (_, _) => 100m,
            benchmarkPriceOf: _ => 100m
        );

        result.EndDate.Should().Be(from.AddYears(HoldingsBacktestCalculator.MaxYears));
        result.Points.Last().Date.Should().BeOnOrBefore(result.EndDate);
    }

    [Fact]
    public void Calculate_FilerStoppedFiling_StopsAtTheStaleTailInsteadOfMarkingAFrozenBookForward()
    {
        // Scion filed its last 13F for 2025-09-30, which matured on 2025-11-14, and then
        // deregistered. A trailing three-year window still runs to today, so the simulation kept
        // trading that final three-stock snapshot for another eight months and reported the result
        // as the manager's track record. A filer that is still filing rebalances every ~91 days, so
        // a gap of 150 says the filings stopped rather than that this is still their portfolio.
        var lastReport = new DateOnly(2025, 9, 30);
        var lastRebalance = HoldingsBacktestCalculator.RebalanceDateOf(lastReport);

        var result = HoldingsBacktestCalculator.Calculate(
            [SingleStockSnapshot(lastReport, StockA, 1_000_000)],
            from: new DateOnly(2025, 11, 20),
            to: new DateOnly(2026, 7, 28),
            priceOf: (_, _) => 100m,
            benchmarkPriceOf: _ => 100m
        );

        var staleAfter = lastRebalance.AddDays(HoldingsBacktestCalculator.StaleTailDays);
        result.TruncatedAt.Should().Be(staleAfter);
        result.EndDate.Should().Be(staleAfter);
        result.Points.Last().Date.Should().Be(staleAfter);
    }

    [Fact]
    public void Calculate_FilerStillCurrent_DoesNotTruncate()
    {
        // The common case must be untouched: a filer whose latest filing has matured inside the
        // normal quarterly cadence keeps the window it was asked for, and reports no truncation.
        var result = HoldingsBacktestCalculator.Calculate(
            [SingleStockSnapshot(new DateOnly(2026, 3, 31), StockA, 1_000_000)],
            from: new DateOnly(2026, 5, 20),
            to: new DateOnly(2026, 7, 28),
            priceOf: (_, _) => 100m,
            benchmarkPriceOf: _ => 100m
        );

        result.TruncatedAt.Should().BeNull();
        result.EndDate.Should().Be(new DateOnly(2026, 7, 28));
    }

    [Fact]
    public void Calculate_SingleSnapshotConstantPrices_ReturnsInitialValue()
    {
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);
        var rebalanceDate = new DateOnly(2024, 5, 15);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(5),
            priceOf: (_, _) => 100m,
            benchmarkPriceOf: _ => 400m
        );

        result.Points.Should().HaveCount(6);
        result.Points.Should().AllSatisfy(p => p.PortfolioValue.Should().Be(100m));
        result.Points.Should().AllSatisfy(p => p.BenchmarkValue.Should().Be(100m));
    }

    [Fact]
    public void Calculate_SingleSnapshotPriceDoubles_PortfolioDoubles()
    {
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);
        var rebalanceDate = new DateOnly(2024, 5, 15);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(3),
            priceOf: (_, day) => day == rebalanceDate ? 100m : 200m,
            benchmarkPriceOf: _ => 50m
        );

        result.Points.First().PortfolioValue.Should().Be(100m);
        result.Points.Last().PortfolioValue.Should().Be(200m);
        result.PortfolioSummary.TotalReturnPercent.Should().Be(100m);
    }

    [Fact]
    public void Calculate_OptionsAreExcluded_FromPortfolio()
    {
        var snapshot = new BacktestQuarterSnapshot
        {
            ReportDate = new DateOnly(2024, 3, 31),
            Positions =
            {
                new BacktestPosition
                {
                    CommonStockId = StockA,
                    Shares = 10_000,
                    Value = 1_000_000,
                    IsOption = false,
                },
                new BacktestPosition
                {
                    CommonStockId = StockB,
                    Shares = 100_000,
                    Value = 10_000_000,
                    IsOption = true,
                },
            },
        };
        var rebalanceDate = new DateOnly(2024, 5, 15);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(5),
            priceOf: (stockId, day) =>
            {
                if (stockId == StockA)
                    return day == rebalanceDate ? 100m : 200m;
                return day == rebalanceDate ? 100m : 10m;
            },
            benchmarkPriceOf: _ => 100m
        );

        result.PortfolioSummary.TotalReturnPercent.Should().Be(100m);
    }

    [Fact]
    public void Calculate_ZeroValuePositions_AreExcluded()
    {
        var snapshot = new BacktestQuarterSnapshot
        {
            ReportDate = new DateOnly(2024, 3, 31),
            Positions =
            {
                new BacktestPosition
                {
                    CommonStockId = StockA,
                    Shares = 10_000,
                    Value = 1_000_000,
                    IsOption = false,
                },
                new BacktestPosition
                {
                    CommonStockId = StockB,
                    Shares = 5_000,
                    Value = 0,
                    IsOption = false,
                },
            },
        };
        var rebalanceDate = new DateOnly(2024, 5, 15);

        // Stock B has zero value so it should be excluded from the portfolio.
        // If it were included, its weight would be zero anyway, but we verify
        // the portfolio is driven entirely by Stock A's doubling.
        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(3),
            priceOf: (stockId, day) =>
            {
                if (stockId == StockA)
                    return day == rebalanceDate ? 100m : 200m;
                return 50m;
            },
            benchmarkPriceOf: _ => 100m
        );

        result.PortfolioSummary.TotalReturnPercent.Should().Be(100m);
    }

    [Fact]
    public void Calculate_NullPriceForHolding_ExcludesFromMarkToMarket()
    {
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);
        var rebalanceDate = new DateOnly(2024, 5, 15);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(3),
            priceOf: (_, day) => day == rebalanceDate ? 100m : null,
            benchmarkPriceOf: _ => 50m
        );

        // When price is null, mark-to-market falls back to the prior portfolio value.
        result.Points.Should().AllSatisfy(p => p.PortfolioValue.Should().Be(100m));
    }

    [Fact]
    public void Calculate_FlatPortfolio_ZeroTotalReturn()
    {
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);
        var rebalanceDate = new DateOnly(2024, 5, 15);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(5),
            priceOf: (_, _) => 100m,
            benchmarkPriceOf: _ => 100m
        );

        result.PortfolioSummary.TotalReturnPercent.Should().Be(0m);
        result.PortfolioSummary.MaxDrawdownPercent.Should().Be(0m);
    }

    [Fact]
    public void Calculate_PortfolioGains50Percent_CorrectTotalReturn()
    {
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);
        var rebalanceDate = new DateOnly(2024, 5, 15);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(3),
            priceOf: (_, day) => day == rebalanceDate ? 100m : 150m,
            benchmarkPriceOf: _ => 100m
        );

        result.PortfolioSummary.TotalReturnPercent.Should().Be(50m);
    }

    [Fact]
    public void Calculate_MaxDrawdown_CalculatedCorrectly()
    {
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);
        var rebalanceDate = new DateOnly(2024, 5, 15);

        // Day 0: 100, Day 1-3: 75, Day 4-5: 100 => max drawdown = 25%
        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(5),
            priceOf: (_, day) =>
            {
                var offset = day.DayNumber - rebalanceDate.DayNumber;
                if (offset == 0)
                    return 100m;
                if (offset is 1 or 2 or 3)
                    return 75m;
                return 100m;
            },
            benchmarkPriceOf: _ => 1m
        );

        result.PortfolioSummary.MaxDrawdownPercent.Should().Be(25m);
        result.PortfolioSummary.TotalReturnPercent.Should().Be(0m);
    }

    [Fact]
    public void Calculate_BenchmarkPriceNull_FallsBackToStartPrice()
    {
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);
        var rebalanceDate = new DateOnly(2024, 5, 15);

        // Benchmark price is 50 on day 0, then null on subsequent days.
        // The calculator should fall back to benchStart (50) when null.
        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(3),
            priceOf: (_, _) => 100m,
            benchmarkPriceOf: day => day == rebalanceDate ? 50m : null
        );

        // benchStart=50, benchPriceToday falls back to 50, so benchValue = InitialValue * (50/50) = 100
        result.Points.Should().AllSatisfy(p => p.BenchmarkValue.Should().Be(100m));
        result.BenchmarkSummary.TotalReturnPercent.Should().Be(0m);
    }

    private static BacktestQuarterSnapshot SingleStockSnapshot(
        DateOnly reportDate,
        Guid stockId,
        long value
    )
    {
        return new BacktestQuarterSnapshot
        {
            ReportDate = reportDate,
            Positions =
            {
                new BacktestPosition
                {
                    CommonStockId = stockId,
                    Shares = 10_000,
                    Value = value,
                    IsOption = false,
                },
            },
        };
    }
}
