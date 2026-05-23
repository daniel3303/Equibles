using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class IndustryAllocationCalculatorZeroValueTests
{
    // During a value-pending refresh all holdings can have Value == 0.
    // totalValue becomes 0 and the PercentOfPortfolio division must not
    // throw or produce NaN/Infinity — the guard `totalValue > 0` should
    // yield 0.0 for every slice. A refactor that drops the guard would
    // produce double.NaN (0 / 0) or double.PositiveInfinity (n / 0).
    [Fact]
    public void Calculate_AllHoldingsZeroValue_PercentOfPortfolioIsZeroNotNaN()
    {
        var industry = new Industry { Id = Guid.NewGuid(), Name = "Software" };
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "TEST",
            Name = "Test Corp.",
            Cik = "0099999999",
            IndustryId = industry.Id,
            Industry = industry,
        };
        var holding = new InstitutionalHolding
        {
            CommonStockId = stock.Id,
            CommonStock = stock,
            InstitutionalHolderId = Guid.NewGuid(),
            FilingDate = new DateOnly(2025, 1, 15),
            ReportDate = new DateOnly(2024, 12, 31),
            Shares = 1_000,
            Value = 0,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
        };

        var result = IndustryAllocationCalculator.Calculate([holding]);

        result.Should().ContainSingle();
        result[0].PositionCount.Should().Be(1);
        result[0].TotalValue.Should().Be(0);
        result[0].PercentOfPortfolio.Should().Be(0.0);
        double.IsNaN(result[0].PercentOfPortfolio).Should().BeFalse();
    }
}
