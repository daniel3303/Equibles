using System.Reflection;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorRebalanceAllOptionsTests
{
    private static readonly MethodInfo RebalanceMethod =
        typeof(HoldingsBacktestCalculator).GetMethod(
            "Rebalance",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // Rebalance's WHY-comment documents: "Options rows are notional and
    // skipped." The filter is `p => !p.IsOption && p.Value > 0`. A
    // single-character flip from `&&` to `||` (or removing the `!p.IsOption`
    // clause entirely) would let option rows enter the per-stock allocation
    // loop and pollute the backtest's holdings dict with derivative
    // exposure — the simulator would mark-to-market notional option Values
    // as cash equity positions. Pin the contract with an all-options
    // snapshot so any regression that bypasses the filter is caught here.
    [Fact]
    public void Rebalance_AllOptionsSnapshot_LeavesHoldingsEmpty()
    {
        var holdings = new Dictionary<Guid, decimal>
        {
            [Guid.NewGuid()] = 42m, // prior entry must be cleared
        };
        var stockId = Guid.NewGuid();
        var snapshot = new BacktestQuarterSnapshot
        {
            ReportDate = new DateOnly(2024, 9, 30),
            Positions =
            [
                new()
                {
                    CommonStockId = stockId,
                    Shares = 100,
                    Value = 1000,
                    IsOption = true,
                },
            ],
        };
        Func<Guid, DateOnly, decimal?> priceOf = (_, _) => 100m;

        RebalanceMethod.Invoke(
            null,
            [holdings, snapshot, new DateOnly(2024, 11, 14), 10000m, priceOf]
        );

        holdings.Should().BeEmpty();
    }
}
