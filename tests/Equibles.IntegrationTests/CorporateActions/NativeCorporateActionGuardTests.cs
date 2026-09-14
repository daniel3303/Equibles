using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Equibles.IntegrationTests.CorporateActions;

[Collection(HistoricalEquityDbCollection.Name)]
public class NativeCorporateActionGuardTests(HistoricalEquityDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Guards_PreserveHistoryAndWorkWithoutRetiredOwnerColumns(bool retireColumns)
    {
        var db = DbContext;
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "GUARDED");
        issuer.Presentation.Listing.TradingCurrency = "USD";
        var other = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "OTHER");
        db.AddRange(issuer, other);
        await db.SaveChangesAsync();
        var at = new DateTime(2025, 2, 1, 12, 34, 56, DateTimeKind.Utc);
        var split = new StockSplit
        {
            EquityIssuerId = issuer.Id,
            EquityListingId = issuer.Presentation.EquityListingId,
            PriceSeriesTicker = "GUARDED",
            EffectiveDate = new DateOnly(2025, 1, 1),
            Numerator = 2,
            Denominator = 1,
            Source = StockSplitSource.Yahoo,
            CreationTime = at.AddYears(-1),
            PriceAdjustmentAppliedTime = at,
        };
        var dividend = new CashDividend
        {
            EquityIssuerId = issuer.Id,
            EquityListingId = issuer.Presentation.EquityListingId,
            Currency = "USD",
            ExDate = split.EffectiveDate,
            AmountPerShare = 1.234567m,
            Source = CashDividendSource.Yahoo,
            CreationTime = at.AddYears(-1),
            PriceAdjustmentAppliedAmountPerShare = 1.234567m,
            PriceAdjustmentAppliedTime = at,
        };
        db.AddRange(split, dividend);
        await db.SaveChangesAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var before = await Snapshot(db);
        foreach (
            var operation in new UseNativeCorporateActionGuards().UpOperations.Cast<SqlOperation>()
        )
            await db.Database.ExecuteSqlRawAsync(operation.Sql);
        (await Snapshot(db))
            .Should()
            .Be(before, "replacing guards must preserve every stored field");
        if (retireColumns)
        {
            // Rehearse the final contract only inside this rollback-only test transaction.
            await db.Database.ExecuteSqlRawAsync(
                """
                DROP TRIGGER "00_equity_owner_columns" ON "StockSplit";
                DROP TRIGGER "equity_identity_series_write" ON "StockSplit";
                DROP TRIGGER "00_equity_owner_columns" ON "CashDividend";
                ALTER TABLE "StockSplit" DROP COLUMN "CommonStockId";
                ALTER TABLE "CashDividend" DROP COLUMN "CommonStockId";
                """
            );
        }
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"StockSplit\" SET \"Numerator\" = \"Numerator\" WHERE \"Id\" = {split.Id}"
        );
        await db.Entry(split).ReloadAsync();
        split.PriceAdjustmentAppliedTime.Should().Be(at);
        split.Numerator = 3;
        await db.SaveChangesAsync();
        await db.Entry(split).ReloadAsync();
        split
            .PriceAdjustmentAppliedTime.Should()
            .BeNull("a revised ratio invalidates the incorporated price basis");
        foreach (
            var invalid in new[] { "owner", "symbol", "listing", "currency", "dividend-owner" }
        )
        {
            await transaction.CreateSavepointAsync("before_invalid");
            db.ChangeTracker.Clear();
            var retainedSplit = await db.Set<StockSplit>().SingleAsync();
            var retainedDividend = await db.Set<CashDividend>().SingleAsync();
            if (invalid == "owner")
                retainedSplit.EquityIssuerId = other.Id;
            if (invalid == "symbol")
                retainedSplit.PriceSeriesTicker = "OTHER";
            if (invalid == "listing")
                retainedSplit.EquityListingId = null;
            if (invalid == "currency")
                retainedDividend.Currency = "EUR";
            if (invalid == "dividend-owner")
                retainedDividend.EquityIssuerId = other.Id;
            var save = () => db.SaveChangesAsync();
            await save.Should().ThrowAsync<DbUpdateException>();
            await transaction.RollbackToSavepointAsync("before_invalid");
        }
        db.ChangeTracker.Clear();
        (await db.Set<StockSplit>().SingleAsync()).EquityIssuerId.Should().Be(issuer.Id);
        var retained = await db.Set<CashDividend>().SingleAsync();
        retained.AmountPerShare.Should().Be(dividend.AmountPerShare);
        retained
            .PriceAdjustmentAppliedAmountPerShare.Should()
            .Be(dividend.PriceAdjustmentAppliedAmountPerShare);
        retained.PriceAdjustmentAppliedTime.Should().Be(at);
        retained.Currency.Should().Be("USD");
    }

    [Theory]
    [InlineData("CommonStockId")]
    [InlineData("EquityIssuerId")]
    public async Task BothWriterGenerations_InvalidateSplitMarkerOnOwnerChange(string ownerColumn)
    {
        var db = DbContext;
        var first = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "FIRST");
        var second = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SECOND");
        db.AddRange(first, second);
        await db.SaveChangesAsync();
        var split = new StockSplit
        {
            EquityIssuerId = first.Id,
            EffectiveDate = new DateOnly(2025, 1, 1),
            Numerator = 2,
            Denominator = 1,
            PriceAdjustmentAppliedTime = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        db.Add(split);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE \"StockSplit\" SET \"{ownerColumn}\" = {{0}} WHERE \"Id\" = {{1}}",
            second.Id,
            split.Id
        );
        await db.Entry(split).ReloadAsync();
        split.EquityIssuerId.Should().Be(second.Id);
        split.PriceAdjustmentAppliedTime.Should().BeNull();
    }

    private static Task<string> Snapshot(EquiblesFinancialDbContext db) =>
        db
            .Database.SqlQueryRaw<string>(
                """
                SELECT jsonb_build_object(
                    'splits', (SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "StockSplit" r),
                    'dividends', (SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "CashDividend" r)
                )::text AS "Value"
                """
            )
            .SingleAsync();
}
