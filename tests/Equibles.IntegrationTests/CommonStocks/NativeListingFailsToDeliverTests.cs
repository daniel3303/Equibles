using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class NativeListingFailsToDeliverTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private EquiblesFinancialDbContext _migrationContext;
    private EquiblesFinancialDbContext Context => _migrationContext ?? DbContext;

    [Fact]
    public async Task Migration_PreservesEveryOriginalField_AndSurvivesLegacyOwnerRemoval()
    {
        await using var database = await IsolatedMigrationDatabase.Create(
            Fixture,
            "20260912003738_RetargetIssuerDisclosures"
        );
        _migrationContext = database.Context;
        var stock = new CommonStock
        {
            Ticker = "FTDA",
            Name = "Original issuer",
            Cik = "0000000093",
            SecondaryTickers = ["FTDB"],
        };
        Context.Add(stock);
        await Context.SaveChangesAsync();
        await InsertLegacy(stock.Id, "FTDA", long.MaxValue);
        await InsertLegacy(stock.Id, "FTDB", 789);
        var original = await Snapshot();

        await ApplyMigration();
        (await Snapshot()).Should().Be(original);
        var listings = await Context
            .Set<LegacyEquityListing>()
            .Where(row => row.CommonStockId == stock.Id)
            .ToListAsync();
        var repository = new FailToDeliverRepository(Context);
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
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "CommonStock" WHERE "Id" = {stock.Id}"""
        );
        (await Snapshot()).Should().Be(original);
        (await repository.GetAll().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Migration_UnattributedHistory_RefusesWithoutChangingOriginalRows()
    {
        await using var database = await IsolatedMigrationDatabase.Create(
            Fixture,
            "20260912003738_RetargetIssuerDisclosures"
        );
        _migrationContext = database.Context;
        var stock = new CommonStock
        {
            Ticker = "FTDU",
            Name = "Unresolved history",
            Cik = "0000000093",
        };
        Context.Add(stock);
        await Context.SaveChangesAsync();
        await InsertLegacy(stock.Id, "", 987);
        var original = await Snapshot();
        Func<Task> apply = ApplyMigration;
        await apply
            .Should()
            .ThrowAsync<PostgresException>()
            .Where(error => error.MessageText.Contains("unresolved listing identities"));
        (await Snapshot()).Should().Be(original);
    }

    [Fact]
    public async Task NativeWriter_KeepsSameTickerVenuesSeparate_AndPreservesUpsertIdentity()
    {
        await using var transaction = await Context.Database.BeginTransactionAsync();
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
        Context.AddRange(first, second);
        await Context.SaveChangesAsync();
        var date = new DateOnly(2026, 8, 3);
        var original = new FailToDeliver
        {
            Listing = first,
            ListedTicker = "SAME",
            SettlementDate = date,
            Quantity = 100,
            Price = 12.123456789m,
        };
        Context.AddRange(
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
        await Context.SaveChangesAsync();
        await Context
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
        Context.ChangeTracker.Clear();
        var repository = new FailToDeliverRepository(Context);
        var updated = await repository.GetByListingId(first.Id).SingleAsync();
        updated.Id.Should().Be(original.Id);
        updated.ListedTicker.Should().Be("SAME");
        updated.Quantity.Should().Be(123);
        updated.Price.Should().Be(7.987654321m);
        (await repository.GetByListingId(second.Id).SingleAsync()).Quantity.Should().Be(200);
        (await Context.Set<CommonStock>().CountAsync()).Should().Be(0);
        (await Context.Set<LegacyEquityListing>().CountAsync()).Should().Be(0);
        await transaction.CreateSavepointAsync("before_delete");
        var delete = async () =>
            await Context
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
        await using var transaction = await Context.Database.BeginTransactionAsync();
        var stock = new CommonStock
        {
            Ticker = "FTDA",
            Name = "Rolling writer",
            Cik = "0000000093",
            SecondaryTickers = ["FTDB"],
        };
        Context.Add(stock);
        await Context.SaveChangesAsync();
        await InsertLegacy(stock.Id, "FTDB", 321);
        var row = await Context.Set<FailToDeliver>().SingleAsync();
        var mapping = await Context
            .Set<LegacyEquityListing>()
            .SingleAsync(item => item.CommonStockId == stock.Id && item.ListedTicker == "FTDB");
        row.EquityListingId.Should().Be(mapping.EquityListingId);
        row.ListedTicker.Should().Be("FTDB");
        row.Quantity.Should().Be(321);
        row.Price.Should().Be(123.123456789m);
    }

    private Task<int> InsertLegacy(Guid owner, string ticker, long quantity) =>
        Context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "FailToDeliver" ("Id", "CommonStockId", "ListedTicker", "SettlementDate", "Quantity", "Price", "CreationTime")
            VALUES ({Guid.NewGuid()}, {owner}, {ticker}, DATE '2026-08-03', {quantity}, 123.123456789, TIMESTAMPTZ '2026-08-04 01:02:03.123456Z')
            """
        );

    private Task ApplyMigration() => Context.Database.MigrateAsync();

    private Task<string> Snapshot() =>
        Context
            .Database.SqlQueryRaw<string>(
                """
                SELECT jsonb_agg(to_jsonb(row) - 'EquityListingId' ORDER BY "Id")::text AS "Value" FROM "FailToDeliver" row
                """
            )
            .SingleAsync();
}
