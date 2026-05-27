using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderQuarterlyActivityCalculatorInitiatedZeroPreviousTests
{
    [Fact]
    public void Group_InitiatedPosition_PopulatesPreviousSharesAndValueAsZero()
    {
        // Contract: when a stock is in current-quarter holdings but not the
        // prior quarter (Initiated), the emitted StockPositionChange must
        // report PreviousShares = 0 and PreviousValue = 0. Internally this
        // is the `previous?.Shares ?? 0` / `previous?.Value ?? 0` null-coalesce
        // inside BuildChange. A refactor that drops the `?.` (e.g. assuming
        // ClassifyChange already screens out Initiated) would NRE on every
        // Initiated row, taking out a holder's entire quarterly activity page.
        // Existing `Group_OnlyCurrent_AllStocksAreInitiated` only asserts the
        // bucket count, leaving the two `0` fields unverified — a regression
        // could quietly carry forward stale prior-quarter values without that
        // assertion (e.g. surfacing yesterday's `previous` row pointer if the
        // dictionary lookup returned a stale ref).
        var aapl = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "C0000320",
        };
        var current = new InstitutionalHolding
        {
            CommonStockId = aapl.Id,
            CommonStock = aapl,
            InstitutionalHolderId = Guid.NewGuid(),
            FilingDate = new DateOnly(2025, 1, 15),
            ReportDate = new DateOnly(2024, 12, 31),
            Shares = 1_000,
            Value = 1_000_000,
            ShareType = ShareType.Shares,
        };

        var result = HolderQuarterlyActivityCalculator.Group([current], []);

        var initiated = result[StockPositionChangeType.Initiated].Single();
        initiated.PreviousShares.Should().Be(0);
        initiated.PreviousValue.Should().Be(0);
    }
}
