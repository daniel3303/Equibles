using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class IndustryAllocationCalculatorTests
{
    [Fact]
    public void Calculate_EmptyHoldings_ReturnsEmptyList()
    {
        var result = IndustryAllocationCalculator.Calculate([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_SingleIndustry_IsOneHundredPercent()
    {
        var industry = new Industry { Name = "Software" };
        var stock = MakeStock(industry);
        var holding = MakeHolding(stock, value: 1_000_000);

        var result = IndustryAllocationCalculator.Calculate([holding]);

        result.Should().ContainSingle();
        result[0].IndustryName.Should().Be("Software");
        result[0].PositionCount.Should().Be(1);
        result[0].TotalValue.Should().Be(1_000_000);
        result[0].PercentOfPortfolio.Should().Be(100.0);
    }

    [Fact]
    public void Calculate_MultipleIndustries_OrderedByValueDescending()
    {
        var software = new Industry { Id = Guid.NewGuid(), Name = "Software" };
        var energy = new Industry { Id = Guid.NewGuid(), Name = "Energy" };
        var holdings = new[]
        {
            MakeHolding(MakeStock(software), value: 1_000_000),
            MakeHolding(MakeStock(software), value: 500_000),
            MakeHolding(MakeStock(energy), value: 300_000),
        };

        var result = IndustryAllocationCalculator.Calculate(holdings);

        result.Should().HaveCount(2);
        result[0].IndustryName.Should().Be("Software");
        result[0].TotalValue.Should().Be(1_500_000);
        result[0].PositionCount.Should().Be(2);
        result[1].IndustryName.Should().Be("Energy");
        result[1].TotalValue.Should().Be(300_000);
        // 1.5M / 1.8M ≈ 83.33%
        result[0].PercentOfPortfolio.Should().BeApproximately(83.33, precision: 0.01);
        result[1].PercentOfPortfolio.Should().BeApproximately(16.67, precision: 0.01);
    }

    [Fact]
    public void Calculate_NullIndustry_ProducesUnclassifiedSlice()
    {
        var holding = MakeHolding(MakeStock(industry: null), value: 250_000);

        var result = IndustryAllocationCalculator.Calculate([holding]);

        result.Should().ContainSingle();
        result[0].IndustryId.Should().BeNull();
        result[0].IndustryName.Should().Be(IndustryAllocationSlice.UnclassifiedName);
    }

    [Fact]
    public void Calculate_UnclassifiedAlwaysLast_EvenWhenItHoldsTheLargestValue()
    {
        var energy = new Industry { Id = Guid.NewGuid(), Name = "Energy" };
        var classified = MakeHolding(MakeStock(energy), value: 100_000);
        var unclassified1 = MakeHolding(MakeStock(industry: null), value: 5_000_000);
        var unclassified2 = MakeHolding(MakeStock(industry: null), value: 2_000_000);

        var result = IndustryAllocationCalculator.Calculate([
            classified,
            unclassified1,
            unclassified2,
        ]);

        result.Should().HaveCount(2);
        result[0].IndustryName.Should().Be("Energy");
        result[1].IndustryName.Should().Be(IndustryAllocationSlice.UnclassifiedName);
        result[1].PositionCount.Should().Be(2);
    }

    [Fact]
    public void Calculate_MultipleRowsSameStock_CountsOnePosition()
    {
        var industry = new Industry { Id = Guid.NewGuid(), Name = "Software" };
        var stock = MakeStock(industry);
        // Same stock reported twice with different InstitutionalHolderIds (multi-manager case).
        var holding1 = MakeHolding(stock, value: 300_000);
        var holding2 = MakeHolding(stock, value: 700_000);

        var result = IndustryAllocationCalculator.Calculate([holding1, holding2]);

        result.Should().ContainSingle();
        result[0].PositionCount.Should().Be(1);
        result[0].TotalValue.Should().Be(1_000_000);
    }

    private static CommonStock MakeStock(Industry industry) =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = "STOCK",
            Name = "Test Corp.",
            Cik = "C" + Guid.NewGuid().ToString("N")[..7],
            IndustryId = industry?.Id,
            Industry = industry,
        };

    private static InstitutionalHolding MakeHolding(CommonStock stock, long value) =>
        new()
        {
            CommonStockId = stock.Id,
            CommonStock = stock,
            InstitutionalHolderId = Guid.NewGuid(),
            FilingDate = new DateOnly(2025, 1, 15),
            ReportDate = new DateOnly(2024, 12, 31),
            Shares = value / 100,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
        };
}
