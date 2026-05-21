using System.Collections;
using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class FundOverlapCalculatorBuildStockRowMinValueTests
{
    // BuildStockRow (extracted in #1618) initialises minValue = long.MaxValue
    // and walks each fund's slice, updating min/max via Math.Min / Math.Max.
    // When ≥2 funds hold the stock at distinct values, the returned MinValue
    // must equal the SMALLEST per-fund value — not long.MaxValue (init only)
    // and not the largest (Min vs Max swapped). The downstream dollar-weighted
    // overlap denominator uses MinValue for `intersectionCount` accumulation;
    // a swap silently inflates the overlap percent.
    [Fact]
    public void BuildStockRow_TwoFundsBothHoldStockAtDifferentValues_MinValueIsSmallerOfTheTwo()
    {
        var stockId = Guid.NewGuid();
        var stock = new CommonStock
        {
            Id = stockId,
            Ticker = "AAPL",
            Name = "Apple Inc.",
        };
        var fund1Aggregate = BuildFundAggregate(
            new InstitutionalHolder { Name = "Fund A" },
            [
                new InstitutionalHolding
                {
                    CommonStockId = stockId,
                    CommonStock = stock,
                    Shares = 100,
                    Value = 100,
                },
            ]
        );
        var fund2Aggregate = BuildFundAggregate(
            new InstitutionalHolder { Name = "Fund B" },
            [
                new InstitutionalHolding
                {
                    CommonStockId = stockId,
                    CommonStock = stock,
                    Shares = 300,
                    Value = 300,
                },
            ]
        );
        var fundAggregateType = fund1Aggregate.GetType();
        var fundAggregates = (IList)
            Activator.CreateInstance(typeof(List<>).MakeGenericType(fundAggregateType));
        fundAggregates.Add(fund1Aggregate);
        fundAggregates.Add(fund2Aggregate);

        var buildStockRow = typeof(FundOverlapCalculator).GetMethod(
            "BuildStockRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = buildStockRow.Invoke(null, [stockId, fundAggregates]);
        var minValue = (long)result.GetType().GetField("Item2").GetValue(result);
        var maxValue = (long)result.GetType().GetField("Item3").GetValue(result);

        minValue.Should().Be(100);
        maxValue.Should().Be(300);
    }

    private static object BuildFundAggregate(
        InstitutionalHolder holder,
        IReadOnlyList<InstitutionalHolding> holdings
    )
    {
        var method = typeof(FundOverlapCalculator).GetMethod(
            "BuildFundAggregate",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var paramType = method.GetParameters()[0].ParameterType;
        var fund = Activator.CreateInstance(paramType, holder, holdings);
        return method.Invoke(null, [fund]);
    }
}
