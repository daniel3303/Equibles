using System.Reflection;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorMarkToMarketNegativePriceTests
{
    [Fact]
    public void MarkToMarket_StockWithNegativePrice_SkipsItAndValuesRemainder()
    {
        // Contract (HoldingsBacktestCalculator.cs:173): `price is null ||
        // price.Value <= 0 → continue`. The OR has two arms; the null arm is
        // pinned elsewhere. The `<= 0` arm exists to defend against vendor
        // data anomalies (negative quotes from sign errors, zero prints from
        // staleness markers). A refactor that narrows the guard to
        // `price is null` — perhaps "vendors don't send negative prices" —
        // would multiply a 100-share holding by -10 and subtract 1000 from
        // an otherwise valid portfolio total, distorting (or, if the bad
        // print equals the positive position, zeroing) the day's chart point
        // and downstream CAGR / MaxDrawdown. Pin: one stock with a valid
        // positive price, one with a negative price → result must equal
        // ONLY the valid leg (100 * 50 = 5000), not 5000 - bad_leg.
        var method = typeof(HoldingsBacktestCalculator).GetMethod(
            "MarkToMarket",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var validId = Guid.NewGuid();
        var badId = Guid.NewGuid();
        var holdings = new Dictionary<Guid, decimal> { [validId] = 100m, [badId] = 200m };
        Func<Guid, DateOnly, decimal?> priceOf = (id, _) => id == validId ? 50m : -10m;

        var result = (decimal)
            method!.Invoke(null, [holdings, new DateOnly(2024, 6, 14), priceOf, 0m]);

        result.Should().Be(5000m);
    }
}
