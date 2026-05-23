using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorTests
{
    private static readonly Guid StockA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StockB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Calculate_EmptySnapshots_ReturnsReasonAndNoPoints()
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
    public void Calculate_FromAfterTo_ReturnsReason()
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
    public void Calculate_NoBenchmarkPrice_ReturnsReason()
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
    public void Calculate_FlatPrices_PortfolioMatchesInitialValueEveryDay()
    {
        // Single stock, prices flat at $100, benchmark flat at $400 — portfolio and
        // benchmark series should both stay pinned to InitialValue across the window.
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);
        var rebalanceDate = new DateOnly(2024, 5, 15); // 2024-03-31 + 45d

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(30),
            priceOf: (_, _) => 100m,
            benchmarkPriceOf: _ => 400m
        );

        result.Points.Should().HaveCount(31);
        result.Points.Should().AllSatisfy(p => p.PortfolioValue.Should().Be(100m));
        result.Points.Should().AllSatisfy(p => p.BenchmarkValue.Should().Be(100m));
        result.PortfolioSummary.TotalReturnPercent.Should().Be(0m);
        result.PortfolioSummary.MaxDrawdownPercent.Should().Be(0m);
        result.BenchmarkSummary.TotalReturnPercent.Should().Be(0m);
    }

    [Fact]
    public void Calculate_PriceDoublesAcrossWindow_PortfolioReturns100Percent()
    {
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);
        var rebalanceDate = new DateOnly(2024, 5, 15);
        var endDate = rebalanceDate.AddDays(60);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: endDate,
            priceOf: (_, day) => day == rebalanceDate ? 100m : 200m,
            benchmarkPriceOf: day => day == rebalanceDate ? 50m : 50m
        );

        result.Points.First().PortfolioValue.Should().Be(100m);
        result.Points.Last().PortfolioValue.Should().Be(200m);
        result.PortfolioSummary.TotalReturnPercent.Should().Be(100m);
        result.PortfolioSummary.MaxDrawdownPercent.Should().Be(0m);
        result.BenchmarkSummary.TotalReturnPercent.Should().Be(0m);
    }

    [Fact]
    public void Calculate_PriceDrop_RecordsMaxDrawdown()
    {
        // Day 0: 100, Day 1-3: 75, Day 4-...: 100 → max drawdown = 25%.
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);
        var rebalanceDate = new DateOnly(2024, 5, 15);

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
    public void Calculate_OptionPositionsAreSkipped()
    {
        // Snapshot has stock A common ($1M) and a put on stock B ($10M notional).
        // The put must be skipped — only stock A drives the portfolio.
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

        // Stock A doubles, stock B (option) would have crashed — if options were counted
        // the portfolio would be -90%. With options skipped portfolio returns +100%.
        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(30),
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
    public void Calculate_HorizonCappedAtTenYears()
    {
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);
        var from = new DateOnly(2024, 5, 15);
        var requestedTo = from.AddYears(25);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: from,
            to: requestedTo,
            priceOf: (_, _) => 100m,
            benchmarkPriceOf: _ => 100m
        );

        // Cap at 10 years from `from` — last point must be at most that far out.
        result.EndDate.Should().Be(from.AddYears(HoldingsBacktestCalculator.MaxYears));
        result.Points.Last().Date.Should().BeOnOrBefore(result.EndDate);
    }

    [Fact]
    public void Calculate_DelistedStockPriceNull_HeldContributionDrops()
    {
        // Stock A delisted on day 1: price null afterwards. The held contribution drops
        // to zero for that day, mark-to-market falls back to the prior portfolio value
        // (the contract documented on the calculator) — so the series remains flat at 100
        // rather than crashing to 0.
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);
        var rebalanceDate = new DateOnly(2024, 5, 15);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(3),
            priceOf: (_, day) => day == rebalanceDate ? 100m : null,
            benchmarkPriceOf: _ => 50m
        );

        result.Points.Should().AllSatisfy(p => p.PortfolioValue.Should().Be(100m));
    }

    [Fact]
    public void Calculate_RebalancesOnNextSnapshotsRebalanceDate()
    {
        // Two snapshots. The first holds stock A flat — second rotates into stock B which
        // doubles. The portfolio must reflect the rotation only after the second
        // rebalance date (Q2 ReportDate 2024-06-30 + 45d = 2024-08-14).
        var q1 = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);
        var q2 = SingleStockSnapshot(new DateOnly(2024, 6, 30), StockB, 1_000_000);
        var firstRebalance = new DateOnly(2024, 5, 15);
        var secondRebalance = new DateOnly(2024, 8, 14);

        var result = HoldingsBacktestCalculator.Calculate(
            [q1, q2],
            from: firstRebalance,
            to: secondRebalance.AddDays(10),
            priceOf: (stockId, day) =>
            {
                if (stockId == StockA)
                    return 100m;
                // Stock B trades at 100 on the rebalance close (the cloner's entry price)
                // and 200 afterwards — so the portfolio doubles only after rotating in.
                return day > secondRebalance ? 200m : 100m;
            },
            benchmarkPriceOf: _ => 100m
        );

        result.Points.First(p => p.Date == firstRebalance).PortfolioValue.Should().Be(100m);
        // First day of stock B holding: portfolio still 100 (rebalances at the same close).
        result.Points.First(p => p.Date == secondRebalance).PortfolioValue.Should().Be(100m);
        // 10 days later stock B has doubled relative to the rebalance price → portfolio doubled.
        result.Points.Last().PortfolioValue.Should().Be(200m);
    }

    [Fact]
    public void Calculate_WindowEntirelyBeforeAnyRebalance_ReturnsReason()
    {
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: new DateOnly(2023, 1, 1),
            to: new DateOnly(2023, 12, 31),
            priceOf: (_, _) => 100m,
            benchmarkPriceOf: _ => 100m
        );

        result.Reason.Should().Contain("no rebalance date falls inside");
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

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(5),
            priceOf: (_, _) => 100m,
            benchmarkPriceOf: _ => 100m
        );

        result.Reason.Should().BeNull();
        result.Points.Should().AllSatisfy(p => p.PortfolioValue.Should().Be(100m));
    }

    [Fact]
    public void Calculate_ZeroPriceForHolding_ExcludesFromMarkToMarket()
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
                    Value = 500_000,
                    IsOption = false,
                },
                new BacktestPosition
                {
                    CommonStockId = StockB,
                    Shares = 10_000,
                    Value = 500_000,
                    IsOption = false,
                },
            },
        };
        var rebalanceDate = new DateOnly(2024, 5, 15);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(3),
            priceOf: (stockId, day) =>
            {
                if (stockId == StockB && day > rebalanceDate)
                    return 0m;
                return 100m;
            },
            benchmarkPriceOf: _ => 100m
        );

        result.Reason.Should().BeNull();
        result.Points.Should().NotBeEmpty();
    }

    [Fact]
    public void Calculate_PortfolioGains50Percent_CorrectTotalReturn()
    {
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);
        var rebalanceDate = new DateOnly(2024, 5, 15);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(2),
            priceOf: (_, day) =>
            {
                var offset = day.DayNumber - rebalanceDate.DayNumber;
                return offset switch
                {
                    0 => 100m,
                    1 => 125m,
                    _ => 150m,
                };
            },
            benchmarkPriceOf: _ => 100m
        );

        result.Reason.Should().BeNull();
        result.PortfolioSummary.TotalReturnPercent.Should().Be(50m);
    }

    [Fact]
    public void Calculate_BenchmarkPriceNull_FallsBackToStartPrice()
    {
        var snapshot = SingleStockSnapshot(new DateOnly(2024, 3, 31), StockA, 1_000_000);
        var rebalanceDate = new DateOnly(2024, 5, 15);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(3),
            priceOf: (_, _) => 100m,
            benchmarkPriceOf: day => day == rebalanceDate ? 100m : null
        );

        result.Reason.Should().BeNull();
        result
            .Points.Where(p => p.Date > rebalanceDate)
            .Should()
            .AllSatisfy(p => p.BenchmarkValue.Should().Be(100m));
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
