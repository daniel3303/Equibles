using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorUninvestablePortfolioTests
{
    private static readonly Guid OptionStock = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // Contract: Rebalance skips option rows ("Options rows are notional and skipped"),
    // so a snapshot made up entirely of options leaves `holdings` empty. The XML doc on
    // Calculate promises graceful degradation when a position cannot be valued. With an
    // empty holdings dict, MarkToMarket's first guard (`holdings.Count == 0 → return
    // fallback`) is what keeps the simulation alive: the uninvestable portfolio must
    // carry InitialValue (100) forward unchanged for every day in the window — never
    // crash, never collapse to 0, never divide by an absent total.
    //
    // The risk this catches: that empty-holdings guard is the one MarkToMarket arm no
    // test reaches through the public Calculate path (existing tests hit the non-empty
    // `sum > 0 ? sum : fallback` arm instead). A refactor that dropped the
    // `holdings.Count == 0` short-circuit would let the empty loop fall through to
    // `sum (0) > 0 ? sum : fallback` — today still returns fallback, but a sibling change
    // returning `sum` unconditionally would silently zero every point of an uninvestable
    // run. Pin the carry-forward: an all-options snapshot keeps PortfolioValue at exactly
    // 100 across the whole window while the benchmark (which is priced) moves, proving the
    // simulation actually iterated rather than bailing early.
    [Fact]
    public void Calculate_SnapshotAllOptions_PortfolioHoldsInitialValueAcrossWindow()
    {
        var from = new DateOnly(2024, 1, 2);
        var to = new DateOnly(2024, 1, 5);
        var reportDate = from.AddDays(-HoldingsBacktestCalculator.RebalanceDelayDays);

        var snapshot = new BacktestQuarterSnapshot
        {
            ReportDate = reportDate,
            Positions =
            {
                new BacktestPosition
                {
                    CommonStockId = OptionStock,
                    Shares = 100,
                    Value = 1_000_000,
                    IsOption = true,
                },
            },
        };

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: from,
            to: to,
            priceOf: (_, _) => 100m,
            // Rising benchmark price so the benchmark series diverges from 100 — confirms
            // the simulation iterated day-by-day instead of short-circuiting.
            benchmarkPriceOf: day => 50m + (day.DayNumber - from.DayNumber)
        );

        result
            .Reason.Should()
            .BeNull("the run is valid — an uninvestable portfolio is not an error");
        result.Points.Should().HaveCount(4);
        result
            .Points.Should()
            .OnlyContain(
                p => p.PortfolioValue == 100m,
                "an all-options snapshot leaves the portfolio uninvested, so it carries InitialValue forward"
            );
        result
            .Points[^1]
            .BenchmarkValue.Should()
            .BeGreaterThan(100m, "the priced benchmark moved while the portfolio stayed flat");
        result.PortfolioSummary.TotalReturnPercent.Should().Be(0m);
    }
}
