using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderQuarterlyActivityCalculatorExitedDeltaValueTests
{
    // An exited position (held last quarter, absent this quarter) must produce
    // a negative DeltaValue equal to -PreviousValue. A regression that swapped
    // CurrentValue/PreviousValue in BuildExitedChange would flip the sign,
    // causing downstream renderers to display an exit as a dollar gain.
    [Fact]
    public void Group_ExitedPosition_DeltaValueIsNegativePreviousValue()
    {
        var aapl = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };

        var result = HolderQuarterlyActivityCalculator.Group(
            [],
            [
                new InstitutionalHolding
                {
                    CommonStockId = aapl.Id,
                    CommonStock = aapl,
                    InstitutionalHolderId = Guid.NewGuid(),
                    FilingDate = new DateOnly(2024, 11, 14),
                    ReportDate = new DateOnly(2024, 9, 30),
                    Shares = 2_000,
                    Value = 450_000,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                },
            ]
        );

        var exited = result[StockPositionChangeType.Exited].Should().ContainSingle().Subject;
        exited.CurrentValue.Should().Be(0);
        exited.PreviousValue.Should().Be(450_000);
        exited.DeltaValue.Should().Be(-450_000);
    }
}
