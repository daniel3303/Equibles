using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorExactListingTests
{
    [Fact]
    public void CalculateByListing_SiblingClasses_UseIndependentPriceSeries()
    {
        var stockId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 1, 1);
        var start = HoldingsBacktestCalculator.RebalanceDateOf(reportDate);
        var end = start.AddDays(1);
        var requestedListings = new HashSet<string>();
        var snapshot = new BacktestQuarterSnapshot
        {
            ReportDate = reportDate,
            Positions =
            [
                new BacktestPosition
                {
                    CommonStockId = stockId,
                    ListedTicker = null,
                    Value = 50,
                },
                new BacktestPosition
                {
                    CommonStockId = stockId,
                    ListedTicker = "BRK-B",
                    Value = 50,
                },
            ],
        };

        var result = HoldingsBacktestCalculator.CalculateByListing(
            [snapshot],
            start,
            end,
            (_, listedTicker, date) =>
            {
                requestedListings.Add(listedTicker ?? "PRIMARY");
                if (listedTicker == "BRK-B")
                    return date == start ? 200m : 400m;
                return 100m;
            },
            _ => 100m
        );

        requestedListings.Should().BeEquivalentTo(["PRIMARY", "BRK-B"]);
        result.PortfolioSummary.TotalReturnPercent.Should().Be(50m);
    }
}
