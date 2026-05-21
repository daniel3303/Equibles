using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class FundOverlapCalculatorBuildFundAggregateMultiDiscretionSumTests
{
    // BuildFundAggregate (extracted in #1610) collapses multiple discretion
    // rows for the same CommonStockId into one FundStockAggregate per
    // doc-comment. The contract: Shares and Value across the rows MUST be
    // summed, while Ticker/Name come from the first row's CommonStock
    // navigation. A refactor that swapped g.Sum for g.First on Shares (or
    // dropped the GroupBy entirely) would silently lose half a fund's
    // position when a holder reports the same stock under two managers.
    [Fact]
    public void BuildFundAggregate_SameStockReportedTwiceUnderDifferentDiscretion_PerStockSumsSharesAndValue()
    {
        var stockId = Guid.NewGuid();
        var stock = new CommonStock
        {
            Id = stockId,
            Ticker = "AAPL",
            Name = "Apple Inc.",
        };
        var holdings = new List<InstitutionalHolding>
        {
            new()
            {
                CommonStockId = stockId,
                CommonStock = stock,
                Shares = 100,
                Value = 1_000,
            },
            new()
            {
                CommonStockId = stockId,
                CommonStock = stock,
                Shares = 200,
                Value = 2_000,
            },
        };
        var holder = new InstitutionalHolder { Name = "Test Fund" };
        var fundType = typeof(FundOverlapCalculator);
        var method = fundType.GetMethod(
            "BuildFundAggregate",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        // The method takes a ValueTuple, build it via reflection on parameter type.
        var paramType = method.GetParameters()[0].ParameterType;
        var fund = Activator.CreateInstance(
            paramType,
            holder,
            (IReadOnlyList<InstitutionalHolding>)holdings
        );

        var result = method.Invoke(null, [fund]);

        var perStock = result.GetType().GetProperty("PerStock").GetValue(result);
        // PerStock is Dictionary<Guid, FundStockAggregate>; index in via reflection.
        var indexer = perStock.GetType().GetProperty("Item");
        var aggregate = indexer.GetValue(perStock, [stockId]);
        var shares = (long)aggregate.GetType().GetProperty("Shares").GetValue(aggregate);
        var value = (long)aggregate.GetType().GetProperty("Value").GetValue(aggregate);

        shares.Should().Be(300);
        value.Should().Be(3_000);
    }
}
