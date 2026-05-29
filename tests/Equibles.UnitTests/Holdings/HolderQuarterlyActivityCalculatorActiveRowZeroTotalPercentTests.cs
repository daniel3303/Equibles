using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderQuarterlyActivityCalculatorActiveRowZeroTotalPercentTests
{
    // PercentOfPortfolio is a percentage of the current-quarter total value
    // (BuildChange: totalCurrentValue > 0 ? (double)current.Value / total * 100 : 0).
    // When an *active* (non-Exited) position exists but the portfolio's total
    // current value is 0 (e.g. a holding carried with no value), the guard's
    // else branch must yield a finite 0 — never NaN from 0.0 / 0. Existing tests
    // only reach the zero-total case via Exited rows, which hardcode 0% in
    // BuildExitedChange, so the BuildChange division guard's else branch is
    // otherwise unexercised. Contract: the percentage is always a finite number.
    [Fact]
    public void Group_InitiatedRowWithZeroTotalCurrentValue_ReturnsFiniteZeroPercent()
    {
        var aapl = MakeStock("AAPL", "Apple Inc.");

        var result = HolderQuarterlyActivityCalculator.Group(
            [MakeHolding(aapl, shares: 1_000, value: 0)],
            []
        );

        var initiated = result[StockPositionChangeType.Initiated].Should().ContainSingle().Subject;
        initiated.Ticker.Should().Be("AAPL");
        double.IsNaN(initiated.PercentOfPortfolio).Should().BeFalse();
        initiated.PercentOfPortfolio.Should().Be(0);
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
