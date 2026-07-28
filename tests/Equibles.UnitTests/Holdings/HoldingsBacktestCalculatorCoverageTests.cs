using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorCoverageTests
{
    private static readonly Guid StockA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StockB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static BacktestQuarterSnapshot Snapshot(
        DateOnly reportDate,
        params (Guid StockId, long Value, bool IsOption)[] positions
    ) =>
        new()
        {
            ReportDate = reportDate,
            Positions = positions
                .Select(p => new BacktestPosition
                {
                    CommonStockId = p.StockId,
                    Shares = 10_000,
                    Value = p.Value,
                    IsOption = p.IsOption,
                })
                .ToList(),
        };

    private static BacktestResult Run(params BacktestQuarterSnapshot[] snapshots) =>
        HoldingsBacktestCalculator.Calculate(
            snapshots,
            from: HoldingsBacktestCalculator.RebalanceDateOf(snapshots[0].ReportDate),
            to: HoldingsBacktestCalculator.RebalanceDateOf(snapshots[^1].ReportDate).AddDays(10),
            priceOf: (_, _) => 100m,
            benchmarkPriceOf: _ => 100m
        );

    [Fact]
    public void Calculate_BookIsMostlyOptions_ReportsHowLittleTheCloneTracks()
    {
        // Scion's Q3 2025 shape: a $912M Palantir put and a $186M NVDA put beside $55M of actual
        // equity. The clone can only buy the equity, so it tracks about 4% of what the manager
        // reported — and the return it produces describes that 4%, not the manager. Without this
        // figure the page has no way to know it should not be advertising the number.
        var result = Run(
            Snapshot(
                new DateOnly(2025, 9, 30),
                (StockA, 912_100_000, true),
                (StockB, 55_000_000, false)
            )
        );

        result.Coverage.QuartersMeasured.Should().Be(1);
        result.Coverage.AverageLongPercent.Should().BeApproximately(5.69m, 0.01m);
        result.Coverage.MinimumLongPercent.Should().BeApproximately(5.69m, 0.01m);
    }

    [Fact]
    public void Calculate_PlainLongBook_ReportsFullCoverage()
    {
        // The ordinary filer. Coverage has to read 100% here or every clone on the platform would
        // carry a warning, and a warning that is always on is a warning nobody reads.
        var result = Run(
            Snapshot(
                new DateOnly(2025, 9, 30),
                (StockA, 1_000_000, false),
                (StockB, 500_000, false)
            )
        );

        result.Coverage.AverageLongPercent.Should().Be(100m);
        result.Coverage.MinimumLongPercent.Should().Be(100m);
    }

    [Fact]
    public void Calculate_CoverageCollapsesInOneQuarter_KeepsTheWorstQuarterVisible()
    {
        // The case the average alone hides, and the one that produced the misleading headline: a
        // book that is fully long for a year and then almost entirely options for the stretch that
        // generated the return. The mean reads like a mild caveat; the minimum says the quarter
        // that mattered was not being tracked at all.
        var result = Run(
            Snapshot(new DateOnly(2024, 12, 31), (StockA, 1_000_000, false)),
            Snapshot(new DateOnly(2025, 3, 31), (StockA, 1_000_000, false)),
            Snapshot(
                new DateOnly(2025, 6, 30),
                (StockA, 990_000_000, true),
                (StockB, 10_000_000, false)
            )
        );

        result.Coverage.QuartersMeasured.Should().Be(3);
        result.Coverage.AverageLongPercent.Should().BeApproximately(66.99m, 0.01m);
        result.Coverage.MinimumLongPercent.Should().BeApproximately(1m, 0.01m);
    }

    [Fact]
    public void Calculate_OnlyOptionsReported_ReportsZeroRatherThanNoMeasurement()
    {
        // An all-options filer produces an empty portfolio, and the calculator already returns a
        // flat line for it. Coverage must say 0% rather than leave the figure unset, because an
        // absent measurement reads to a caller exactly like "nothing to warn about".
        var result = Run(Snapshot(new DateOnly(2025, 9, 30), (StockA, 912_100_000, true)));

        result.Coverage.QuartersMeasured.Should().Be(1);
        result.Coverage.AverageLongPercent.Should().Be(0m);
    }

    [Fact]
    public void Calculate_SnapshotsOutsideTheWindow_AreNotCounted()
    {
        // Coverage describes what THIS result covers. A later quarter the simulation never reached
        // must not dilute it, or a window ending before a filer pivoted into options would inherit
        // a warning it does not deserve — and, worse, the reverse.
        var result = HoldingsBacktestCalculator.Calculate(
            [
                Snapshot(new DateOnly(2024, 12, 31), (StockA, 1_000_000, false)),
                Snapshot(new DateOnly(2025, 6, 30), (StockA, 1_000_000, true)),
            ],
            from: HoldingsBacktestCalculator.RebalanceDateOf(new DateOnly(2024, 12, 31)),
            to: HoldingsBacktestCalculator.RebalanceDateOf(new DateOnly(2025, 6, 30)).AddDays(-1),
            priceOf: (_, _) => 100m,
            benchmarkPriceOf: _ => 100m
        );

        result.Coverage.QuartersMeasured.Should().Be(1);
        result.Coverage.AverageLongPercent.Should().Be(100m);
    }
}
