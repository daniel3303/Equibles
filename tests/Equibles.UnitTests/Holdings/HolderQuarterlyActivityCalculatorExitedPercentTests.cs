using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderQuarterlyActivityCalculatorExitedPercentTests
{
    // Source contract (HolderQuarterlyActivityCalculator.cs:28-30): "Anchoring on the
    // current side keeps comparisons consistent for all four movement buckets except
    // Exited; Exited rows show 0% (their current value is 0)." A refactor that
    // consolidated BuildChange/BuildExitedChange and anchored Exited on previous
    // value would silently emit non-zero percentages — Group_OnlyPrevious_AllStocksAreExited
    // can't catch it because its totalCurrentValue is 0 and a divide-by-zero guard would
    // still return 0. This case keeps a current position so totalCurrentValue > 0.
    [Fact]
    public void Group_ExitedRowWithNonZeroCurrentTotal_ReturnsZeroPercentOfPortfolio()
    {
        var aapl = MakeStock("AAPL", "Apple Inc.");
        var msft = MakeStock("MSFT", "Microsoft Corp.");

        var result = HolderQuarterlyActivityCalculator.Group(
            [MakeHolding(aapl, shares: 1_000, value: 1_000_000)],
            [
                MakeHolding(aapl, shares: 1_000, value: 1_000_000),
                MakeHolding(msft, shares: 500, value: 500_000),
            ]
        );

        var exited = result[StockPositionChangeType.Exited].Should().ContainSingle().Subject;
        exited.Ticker.Should().Be("MSFT");
        exited.PercentOfPortfolio.Should().Be(0);
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
