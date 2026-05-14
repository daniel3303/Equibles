using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Isolated repro for the FlexLabs <c>UpsertRange(...).On(...).WhenMatched(...)</c> path
/// FtdImportService relies on. The Postgres backend should compile this into
/// <c>INSERT ... ON CONFLICT (cols) DO UPDATE SET ...</c> and update the matched row.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FailToDeliverUpsertWhenMatchedTests : ParadeDbMcpTestBase
{
    public FailToDeliverUpsertWhenMatchedTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact(
        Skip = "GH-481 — UpsertRange WhenMatched silently no-ops on existing FailToDeliver rows against ParadeDB (FtdImportService cannot correct stale Quantity/Price on re-import)"
    )]
    public async Task UpsertRange_OnCommonStockIdAndSettlementDate_WhenMatched_OverwritesExistingRowQuantityAndPrice()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var settlementDate = new DateOnly(2026, 4, 1);
        var existing = new FailToDeliver
        {
            CommonStockId = stock.Id,
            SettlementDate = settlementDate,
            Quantity = 999,
            Price = 10.00m,
        };

        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<FailToDeliver>().Add(existing);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await DbContext
            .Set<FailToDeliver>()
            .UpsertRange([
                new FailToDeliver
                {
                    CommonStockId = stock.Id,
                    SettlementDate = settlementDate,
                    Quantity = 12345,
                    Price = 187.50m,
                },
            ])
            .On(f => new { f.CommonStockId, f.SettlementDate })
            .WhenMatched(f => new FailToDeliver { Quantity = f.Quantity, Price = f.Price })
            .RunAsync();

        DbContext.ChangeTracker.Clear();
        var rows = await DbContext.Set<FailToDeliver>().ToListAsync();

        rows.Should()
            .ContainSingle(
                "conflict on the unique index must collapse INSERT+existing into one row"
            );
        rows[0].Quantity.Should().Be(12345, "WhenMatched projects Quantity = source.Quantity");
        rows[0].Price.Should().Be(187.50m, "WhenMatched projects Price = source.Price");
    }
}
