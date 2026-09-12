using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class NativeListingFailsToDeliverTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    [Fact]
    public async Task Migration_PreservesEveryOriginalField_AndSurvivesLegacyOwnerRemoval()
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        var stock = new CommonStock
        {
            Ticker = "FTDA",
            Name = "Original issuer",
            Cik = "0000000093",
            SecondaryTickers = ["FTDB"],
        };
        DbContext.Add(stock);
        await DbContext.SaveChangesAsync();
        await RestoreLegacySchema();
        await InsertLegacy(stock.Id, "FTDA", long.MaxValue);
        await InsertLegacy(stock.Id, "FTDB", 789);
        var original = await Snapshot();

        await ApplyMigration();
        (await Snapshot()).Should().Be(original);
        var listings = await DbContext
            .Set<LegacyEquityListing>()
            .Where(row => row.CommonStockId == stock.Id)
            .ToListAsync();
        var repository = new FailToDeliverRepository(DbContext);
        (
            await repository
                .GetByListingId(listings.Single(row => row.ListedTicker == "FTDA").EquityListingId)
                .SingleAsync()
        )
            .Quantity.Should()
            .Be(long.MaxValue);
        (
            await repository
                .GetByListingId(listings.Single(row => row.ListedTicker == "FTDB").EquityListingId)
                .SingleAsync()
        )
            .Quantity.Should()
            .Be(789);
        await DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "CommonStock" WHERE "Id" = {stock.Id}"""
        );
        (await Snapshot()).Should().Be(original);
        (await repository.GetAll().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Migration_UnattributedHistory_RefusesWithoutChangingOriginalRows()
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        var stock = new CommonStock
        {
            Ticker = "FTDU",
            Name = "Unresolved history",
            Cik = "0000000093",
        };
        DbContext.Add(stock);
        await DbContext.SaveChangesAsync();
        await RestoreLegacySchema();
        await InsertLegacy(stock.Id, "", 987);
        var original = await Snapshot();
        await transaction.CreateSavepointAsync("before_migration");
        Func<Task> apply = ApplyMigration;
        await apply
            .Should()
            .ThrowAsync<PostgresException>()
            .Where(error => error.MessageText.Contains("unresolved listing identities"));
        await transaction.RollbackToSavepointAsync("before_migration");
        (await Snapshot()).Should().Be(original);
    }

    [Fact]
    public async Task NativeWriter_KeepsSameTickerVenuesSeparate_AndPreservesUpsertIdentity()
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        var issuer = new EquityIssuer { Name = "Native issuer" };
        var security = new EquitySecurity { Issuer = issuer };
        var first = new EquityListing
        {
            Security = security,
            Ticker = "SAME",
            MarketIdentifierCode = "XNYS",
        };
        var second = new EquityListing
        {
            Security = security,
            Ticker = "SAME",
            MarketIdentifierCode = "XLIS",
        };
        DbContext.AddRange(first, second);
        await DbContext.SaveChangesAsync();
        var date = new DateOnly(2026, 8, 3);
        var original = new FailToDeliver
        {
            Listing = first,
            ListedTicker = "SAME",
            SettlementDate = date,
            Quantity = 100,
            Price = 12.123456789m,
        };
        DbContext.AddRange(
            original,
            new FailToDeliver
            {
                Listing = second,
                ListedTicker = "SAME",
                SettlementDate = date,
                Quantity = 200,
                Price = 3.25m,
            }
        );
        await DbContext.SaveChangesAsync();
        await DbContext
            .Set<FailToDeliver>()
            .Upsert(
                new FailToDeliver
                {
                    EquityListingId = first.Id,
                    ListedTicker = "SOURCE-NEW",
                    SettlementDate = date,
                    Quantity = 123,
                    Price = 7.987654321m,
                }
            )
            .On(row => new { row.EquityListingId, row.SettlementDate })
            .WhenMatched(
                (stored, incoming) =>
                    new FailToDeliver { Quantity = incoming.Quantity, Price = incoming.Price }
            )
            .RunAsync();
        DbContext.ChangeTracker.Clear();
        var repository = new FailToDeliverRepository(DbContext);
        var updated = await repository.GetByListingId(first.Id).SingleAsync();
        updated.Id.Should().Be(original.Id);
        updated.ListedTicker.Should().Be("SAME");
        updated.Quantity.Should().Be(123);
        updated.Price.Should().Be(7.987654321m);
        (await repository.GetByListingId(second.Id).SingleAsync()).Quantity.Should().Be(200);
        (await DbContext.Set<CommonStock>().CountAsync()).Should().Be(0);
        (await DbContext.Set<LegacyEquityListing>().CountAsync()).Should().Be(0);
        await transaction.CreateSavepointAsync("before_delete");
        var delete = async () =>
            await DbContext
                .Set<EquityListing>()
                .Where(row => row.Id == first.Id)
                .ExecuteDeleteAsync();
        await delete
            .Should()
            .ThrowAsync<PostgresException>()
            .Where(error => error.SqlState == PostgresErrorCodes.RestrictViolation);
        await transaction.RollbackToSavepointAsync("before_delete");
        (await repository.GetAll().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task RetiringWriter_ReceivesExactListingIdentity_WithoutRewritingItsObservation()
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        var stock = new CommonStock
        {
            Ticker = "FTDA",
            Name = "Rolling writer",
            Cik = "0000000093",
            SecondaryTickers = ["FTDB"],
        };
        DbContext.Add(stock);
        await DbContext.SaveChangesAsync();
        await InsertLegacy(stock.Id, "FTDB", 321);
        var row = await DbContext.Set<FailToDeliver>().SingleAsync();
        var mapping = await DbContext
            .Set<LegacyEquityListing>()
            .SingleAsync(item => item.CommonStockId == stock.Id && item.ListedTicker == "FTDB");
        row.EquityListingId.Should().Be(mapping.EquityListingId);
        row.ListedTicker.Should().Be("FTDB");
        row.Quantity.Should().Be(321);
        row.Price.Should().Be(123.123456789m);
    }

    private Task<int> InsertLegacy(Guid owner, string ticker, long quantity) =>
        DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "FailToDeliver" ("Id", "CommonStockId", "ListedTicker", "SettlementDate", "Quantity", "Price", "CreationTime")
            VALUES ({Guid.NewGuid()}, {owner}, {ticker}, DATE '2026-08-03', {quantity}, 123.123456789, TIMESTAMPTZ '2026-08-04 01:02:03.123456Z')
            """
        );

    private Task<int> RestoreLegacySchema() =>
        DbContext.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER equity_ftd_listing_bridge ON "FailToDeliver";
            DROP FUNCTION public.eq_bridge_ftd_listing();
            ALTER TABLE "FailToDeliver" DROP CONSTRAINT "FK_FailToDeliver_EquityListing_EquityListingId";
            DROP INDEX "IX_FailToDeliver_EquityListingId_SettlementDate";
            ALTER TABLE "FailToDeliver" DROP COLUMN "EquityListingId";
            ALTER TABLE "FailToDeliver" ADD CONSTRAINT "FK_FailToDeliver_CommonStock_CommonStockId"
                FOREIGN KEY ("CommonStockId") REFERENCES "CommonStock"("Id") ON DELETE CASCADE;
            """
        );

    private async Task ApplyMigration()
    {
        foreach (
            var command in DbContext
                .GetService<IMigrationsSqlGenerator>()
                .Generate(new RetargetFailsToDeliverToListings().UpOperations)
        )
            await DbContext.Database.ExecuteSqlRawAsync(command.CommandText);
    }

    private Task<string> Snapshot() =>
        DbContext
            .Database.SqlQueryRaw<string>(
                """
                SELECT jsonb_agg(to_jsonb(row) - 'EquityListingId' ORDER BY "Id")::text AS "Value" FROM "FailToDeliver" row
                """
            )
            .SingleAsync();
}
