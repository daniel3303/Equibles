using System.Reflection;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorMarkToMarketAllNullPricesTests
{
    [Fact]
    public void MarkToMarket_HoldingsWithAllNullPrices_ReturnsFallbackNotZero()
    {
        // HoldingsBacktestCalculator.MarkToMarket is invoked on every
        // simulation day to re-value a non-empty portfolio. The price
        // provider routinely returns null on weekends, holidays, halted
        // tickers, and dates before/after a ticker's quoted lifetime — a
        // single backtest run touches all of these. The helper's final
        // `sum > 0 ? sum : fallback` ternary documents the carry-forward
        // semantics: when NO held stock has a usable price for the given
        // date, the portfolio value MUST carry the prior day's `fallback`
        // forward, not collapse to zero.
        //
        // The risk this catches: a refactor that drops the
        //   sum > 0 ? sum : fallback
        // tail and returns `sum` unconditionally (perhaps under the false
        // intuition that "if the sum is zero the portfolio is zero —
        // honest accounting") would compile, pass any test that supplies
        // valid prices for every stock, and on the first weekend/holiday
        // in the simulated window instantly zero the portfolio. The next
        // day's MarkToMarket would re-compute correctly from the same
        // holdings, but the points emitted for the zero-priced days would
        // already be persisted into BacktestPoint.PortfolioValue=0 —
        // corrupting the rendered chart and CAGR/MaxDrawdown calculations
        // that consume the points downstream.
        //
        // Pin the fallback arm: a non-empty holdings dictionary whose
        // priceOf returns null for every stock must produce `fallback`,
        // not `0`. Use a fallback value distinct from zero so a wrong
        // return (e.g. 0) is unambiguous.
        var method = typeof(HoldingsBacktestCalculator).GetMethod(
            "MarkToMarket",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var holdings = new Dictionary<Guid, decimal>
        {
            [Guid.NewGuid()] = 100m,
            [Guid.NewGuid()] = 250m,
        };
        Func<Guid, DateOnly, decimal?> priceOf = (_, _) => null;
        const decimal fallback = 42_000m;

        var result = (decimal)
            method.Invoke(null, [holdings, new DateOnly(2024, 12, 28), priceOf, fallback]);

        result.Should().Be(fallback);
    }
}
