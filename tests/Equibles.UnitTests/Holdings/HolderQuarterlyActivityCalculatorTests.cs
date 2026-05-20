using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderQuarterlyActivityCalculatorTests
{
    [Fact]
    public void Group_BothInputsEmpty_ReturnsEmptyBuckets()
    {
        var result = HolderQuarterlyActivityCalculator.Group([], []);

        result[StockPositionChangeType.Initiated].Should().BeEmpty();
        result[StockPositionChangeType.Increased].Should().BeEmpty();
        result[StockPositionChangeType.Reduced].Should().BeEmpty();
        result[StockPositionChangeType.Exited].Should().BeEmpty();
        result[StockPositionChangeType.Unchanged].Should().BeEmpty();
    }

    [Fact]
    public void Group_OnlyCurrent_AllStocksAreInitiated()
    {
        var aapl = MakeStock("AAPL", "Apple Inc.");
        var msft = MakeStock("MSFT", "Microsoft Corp.");

        var result = HolderQuarterlyActivityCalculator.Group(
            [
                MakeHolding(aapl, shares: 1_000, value: 1_000_000),
                MakeHolding(msft, shares: 500, value: 500_000),
            ],
            []
        );

        result[StockPositionChangeType.Initiated].Should().HaveCount(2);
        result[StockPositionChangeType.Exited].Should().BeEmpty();
    }

    [Fact]
    public void Group_OnlyPrevious_AllStocksAreExited()
    {
        var aapl = MakeStock("AAPL", "Apple Inc.");

        var result = HolderQuarterlyActivityCalculator.Group(
            [],
            [MakeHolding(aapl, shares: 1_000, value: 1_000_000)]
        );

        result[StockPositionChangeType.Exited].Should().ContainSingle();
        result[StockPositionChangeType.Exited][0].PreviousShares.Should().Be(1_000);
        result[StockPositionChangeType.Exited][0].CurrentShares.Should().Be(0);
        result[StockPositionChangeType.Exited][0].DeltaShares.Should().Be(-1_000);
    }

    [Fact]
    public void Group_HigherCurrentShares_ClassifiesAsIncreased()
    {
        var aapl = MakeStock("AAPL", "Apple Inc.");

        var result = HolderQuarterlyActivityCalculator.Group(
            [MakeHolding(aapl, shares: 1_500, value: 1_500_000)],
            [MakeHolding(aapl, shares: 1_000, value: 1_000_000)]
        );

        result[StockPositionChangeType.Increased].Should().ContainSingle();
        result[StockPositionChangeType.Increased][0].DeltaShares.Should().Be(500);
    }

    [Fact]
    public void Group_LowerCurrentShares_ClassifiesAsReduced()
    {
        var aapl = MakeStock("AAPL", "Apple Inc.");

        var result = HolderQuarterlyActivityCalculator.Group(
            [MakeHolding(aapl, shares: 600, value: 600_000)],
            [MakeHolding(aapl, shares: 1_000, value: 1_000_000)]
        );

        result[StockPositionChangeType.Reduced].Should().ContainSingle();
        result[StockPositionChangeType.Reduced][0].DeltaShares.Should().Be(-400);
    }

    [Fact]
    public void Group_PercentOfPortfolio_DividesByCurrentQuarterTotalValue()
    {
        var aapl = MakeStock("AAPL", "Apple Inc.");
        var msft = MakeStock("MSFT", "Microsoft Corp.");

        var result = HolderQuarterlyActivityCalculator.Group(
            [
                MakeHolding(aapl, shares: 1_000, value: 700_000),
                MakeHolding(msft, shares: 500, value: 300_000),
            ],
            []
        );

        var aaplRow = result[StockPositionChangeType.Initiated].Single(r => r.Ticker == "AAPL");
        var msftRow = result[StockPositionChangeType.Initiated].Single(r => r.Ticker == "MSFT");
        aaplRow.PercentOfPortfolio.Should().Be(70.0);
        msftRow.PercentOfPortfolio.Should().Be(30.0);
    }

    [Fact]
    public void Group_MultipleRowsSameStock_AggregatedIntoOneEntry()
    {
        var aapl = MakeStock("AAPL", "Apple Inc.");

        var result = HolderQuarterlyActivityCalculator.Group(
            [
                MakeHolding(aapl, shares: 600, value: 600_000),
                MakeHolding(aapl, shares: 400, value: 400_000),
            ],
            [MakeHolding(aapl, shares: 1_000, value: 1_000_000)]
        );

        // 600 + 400 = 1_000 = prior → Unchanged.
        result[StockPositionChangeType.Unchanged].Should().ContainSingle();
        result[StockPositionChangeType.Unchanged][0].CurrentShares.Should().Be(1_000);
    }

    private static CommonStock MakeStock(string ticker, string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
            Cik = "C" + Guid.NewGuid().ToString("N")[..7],
        };

    private static InstitutionalHolding MakeHolding(CommonStock stock, long shares, long value) =>
        new()
        {
            CommonStockId = stock.Id,
            CommonStock = stock,
            InstitutionalHolderId = Guid.NewGuid(),
            FilingDate = new DateOnly(2025, 1, 15),
            ReportDate = new DateOnly(2024, 12, 31),
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
        };
}
