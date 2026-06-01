using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class SmartMoneyIndexCalculatorConsensusDedupTests
{
    private static readonly Guid StockA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // Contract (class summary + the per-stock GroupBy at Compose): "collapse a fund's
    // multiple discretion rows for the same stock into one position" — so consensus
    // counts FUNDS, not rows. A single 13F filer routinely reports the same CUSIP across
    // sole/shared/no-discretion rows; if those rows each bumped HeldByCount, one filer
    // could manufacture a false multi-fund consensus. With a single fund holding StockA
    // in two rows and a 2-fund consensus floor, StockA must stay below the threshold and
    // be excluded — not promoted by row-counting.
    [Fact]
    public void Compose_SingleFundWithDuplicateStockRows_DoesNotInflateConsensus()
    {
        var fund = new BacktestQuarterSnapshot
        {
            ReportDate = new DateOnly(2025, 3, 31),
            Positions =
            [
                new BacktestPosition
                {
                    CommonStockId = StockA,
                    Value = 60,
                    IsOption = false,
                },
                new BacktestPosition
                {
                    CommonStockId = StockA,
                    Value = 40,
                    IsOption = false,
                },
            ],
        };

        var constituents = SmartMoneyIndexCalculator.Compose(
            [fund],
            maxConstituents: 10,
            minConsensus: 2
        );

        constituents.Should().BeEmpty();
    }
}
