using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderQuarterlyActivityCalculatorMixedExitedTests
{
    // Sibling to Group_OnlyPrevious_AllStocksAreExited. That pin covers the
    // "empty current quarter" path — every prior stock exits. The mixed case
    // (some stocks active in current, others exited from prior) is unpinned.
    // A refactor that gates the Exited foreach on `currentByStock.Count == 0`
    // (an intuitive but wrong "early-out" optimization) would still pass the
    // empty-current pin yet silently drop every Exited classification on any
    // realistic quarter where the holder continues at least one position.
    [Fact]
    public void Group_StockInPreviousNotInCurrent_AlongsideIncreasedStock_ExitsBucketStillPopulated()
    {
        var aapl = MakeStock("AAPL", "Apple Inc.");
        var msft = MakeStock("MSFT", "Microsoft Corp.");

        var result = HolderQuarterlyActivityCalculator.Group(
            [MakeHolding(aapl, shares: 1_500, value: 1_500_000)],
            [
                MakeHolding(aapl, shares: 1_000, value: 1_000_000),
                MakeHolding(msft, shares: 500, value: 500_000),
            ]
        );

        result[StockPositionChangeType.Increased].Should().ContainSingle();
        result[StockPositionChangeType.Increased][0].Ticker.Should().Be("AAPL");
        result[StockPositionChangeType.Exited].Should().ContainSingle();
        result[StockPositionChangeType.Exited][0].Ticker.Should().Be("MSFT");
        result[StockPositionChangeType.Exited][0].PreviousShares.Should().Be(500);
        result[StockPositionChangeType.Exited][0].CurrentShares.Should().Be(0);
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
