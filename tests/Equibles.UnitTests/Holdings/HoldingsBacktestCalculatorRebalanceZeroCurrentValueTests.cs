using System.Reflection;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorRebalanceZeroCurrentValueTests
{
    private static readonly MethodInfo RebalanceMethod =
        typeof(HoldingsBacktestCalculator).GetMethod(
            "Rebalance",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // Rebalance's `if (currentValue <= 0) return;` short-circuit fires the
    // moment the simulated portfolio has gone to zero (or below), which can
    // happen after a long run of mark-to-market wipeouts on illiquid stocks
    // with forward-filled stale prices. The early return is load-bearing:
    // without it, the allocation loop multiplies `currentValue (0) * weight`
    // and divides by price, leaving the holdings dict full of zero-share
    // entries that pin downstream `holdings.Count == 0` checks to false —
    // including MarkToMarket's empty-bail (line 167). A regression that
    // dropped this early return would silently flip the wiped-out portfolio
    // from "no positions" to "positions of zero shares", corrupting every
    // subsequent day's MTM accounting in the simulation.
    [Fact]
    public void Rebalance_ZeroCurrentValueWithPositiveSnapshot_LeavesHoldingsEmpty()
    {
        var holdings = new Dictionary<Guid, decimal> { [Guid.NewGuid()] = 42m };
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
                    IsOption = false,
                },
            ],
        };
        Func<Guid, DateOnly, decimal?> priceOf = (_, _) => 100m;

        RebalanceMethod.Invoke(null, [holdings, snapshot, new DateOnly(2024, 11, 14), 0m, priceOf]);

        holdings.Should().BeEmpty();
    }
}
