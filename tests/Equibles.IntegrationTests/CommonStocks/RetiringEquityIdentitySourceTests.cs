using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class RetiringEquityIdentitySourceTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    [Fact]
    public async Task ListingSource_RetainsOriginalAndRenamedKeysAfterSourceDeletion_AndReplayIsIdempotent()
    {
        var db = DbContext;
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "ORIGINAL");
        db.Add(issuer);
        await db.SaveChangesAsync();
        var listingId = issuer.Presentation.EquityListingId;
        db.Add(
            new LegacyEquityListing
            {
                CommonStockId = issuer.Id,
                ListedTicker = "ORIGINAL",
                EquityListingId = listingId,
            }
        );
        await db.SaveChangesAsync();
        var original = await ListingSnapshot(db);
        await AssertArchived(db, "legacy-equity-listing-v1", original);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"LegacyEquityListing\" SET \"ListedTicker\" = 'RENAMED' WHERE \"EquityListingId\" = {listingId}"
        );
        var renamed = await ListingSnapshot(db);
        await AssertArchived(db, "legacy-equity-listing-v1", renamed);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"LegacyEquityListing\" WHERE \"EquityListingId\" = {listingId}"
        );
        db.ChangeTracker.Clear();
        await AssertArchived(db, "legacy-equity-listing-v1", original);
        await AssertArchived(db, "legacy-equity-listing-v1", renamed);
        (await db.Set<EquityListing>().SingleAsync()).Id.Should().Be(listingId);
        var count = await db.Set<EquityDirectorySourceRecord>().CountAsync();
        await Replay(db);
        await Replay(db);
        (await db.Set<EquityDirectorySourceRecord>().CountAsync()).Should().Be(count);
        await AssertAudit(db);
    }

    [Fact]
    public async Task NativeCursor_DoesNotRewriteOriginalFrontier_AndRetainsFullSourceVersions()
    {
        var db = DbContext;
        var oldOwner = Guid.NewGuid();
        var nextListing = Guid.NewGuid();
        var cursor = new CorporateActionPriceReconciliationCursor
        {
            Name = "Source preservation",
            UpdatedAt = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        };
        db.Add(cursor);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"CorporateActionPriceReconciliationCursor\" SET \"LastCommonStockId\" = {oldOwner}, \"LastListedTicker\" = 'FORMER' WHERE \"Name\" = {cursor.Name}"
        );
        var original = await CursorSnapshot(db, cursor.Name);
        await AssertArchived(db, "corporate-action-cursor-v1", original);
        var model = db.Model.FindEntityType(typeof(CorporateActionPriceReconciliationCursor));
        model.FindProperty("LastCommonStockId").Should().BeNull();
        model.FindProperty("LastListedTicker").Should().BeNull();
        cursor.LastEquityListingId = nextListing;
        cursor.UpdatedAt = cursor.UpdatedAt.Value.AddHours(1);
        await db.SaveChangesAsync();
        var current = await CursorSnapshot(db, cursor.Name);
        await AssertArchived(db, "corporate-action-cursor-v1", current);
        await AssertArchived(db, "corporate-action-cursor-v1", original);
        var retainedOwner = await db
            .Database.SqlQuery<Guid>(
                $"SELECT \"LastCommonStockId\" AS \"Value\" FROM \"CorporateActionPriceReconciliationCursor\" WHERE \"Name\" = {cursor.Name}"
            )
            .SingleAsync();
        retainedOwner.Should().Be(oldOwner);
        var retainedTicker = await db
            .Database.SqlQuery<string>(
                $"SELECT \"LastListedTicker\" AS \"Value\" FROM \"CorporateActionPriceReconciliationCursor\" WHERE \"Name\" = {cursor.Name}"
            )
            .SingleAsync();
        retainedTicker.Should().Be("FORMER");
        await Replay(db);
        (await CursorSnapshot(db, cursor.Name)).Should().Be(current);
        await using var auditTransaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(
            "SET LOCAL timezone = 'Pacific/Auckland'; SET LOCAL extra_float_digits = 0;"
        );
        (
            await db
                .Database.SqlQueryRaw<string>("SELECT current_setting('TimeZone') AS \"Value\"")
                .SingleAsync()
        )
            .Should()
            .Be("Pacific/Auckland");
        await AssertAudit(db);
    }

    [Fact]
    public async Task ExistingSourceRows_AreArchivedBeforeGuardInstallation_WithoutFieldChanges()
    {
        var db = DbContext;
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "PREEXISTING");
        db.Add(issuer);
        await db.SaveChangesAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER equity_retiring_listing_source ON "LegacyEquityListing";
            DROP TRIGGER equity_retiring_cursor_source ON "CorporateActionPriceReconciliationCursor";
            """
        );
        db.Add(
            new LegacyEquityListing
            {
                CommonStockId = issuer.Id,
                ListedTicker = "PREEXISTING",
                EquityListingId = issuer.Presentation.EquityListingId,
            }
        );
        var cursor = new CorporateActionPriceReconciliationCursor { Name = "Before capture" };
        db.Add(cursor);
        await db.SaveChangesAsync();
        var beforeListing = await ListingSnapshot(db);
        var beforeCursor = await CursorSnapshot(db, cursor.Name);
        await transaction.CreateSavepointAsync("before_failed_audit");
        var audit = () => AssertAudit(db);
        await audit.Should().ThrowAsync<Npgsql.PostgresException>();
        await transaction.RollbackToSavepointAsync("before_failed_audit");
        await Replay(db);
        (await ListingSnapshot(db)).Should().Be(beforeListing);
        (await CursorSnapshot(db, cursor.Name)).Should().Be(beforeCursor);
        await AssertArchived(db, "legacy-equity-listing-v1", beforeListing);
        await AssertArchived(db, "corporate-action-cursor-v1", beforeCursor);
        await AssertAudit(db);
    }

    private static Task<string> ListingSnapshot(EquiblesFinancialDbContext db) =>
        db
            .Database.SqlQueryRaw<string>(
                "SELECT to_jsonb(r)::text AS \"Value\" FROM \"LegacyEquityListing\" r"
            )
            .SingleAsync();

    private static Task<string> CursorSnapshot(EquiblesFinancialDbContext db, string name) =>
        db
            .Database.SqlQuery<string>(
                $"SELECT to_jsonb(r)::text AS \"Value\" FROM \"CorporateActionPriceReconciliationCursor\" r WHERE \"Name\" = {name}"
            )
            .SingleAsync();

    private static async Task AssertArchived(
        EquiblesFinancialDbContext db,
        string source,
        string payload
    )
    {
        var count = await db
            .Database.SqlQuery<int>(
                $"SELECT count(*)::integer AS \"Value\" FROM \"EquityDirectorySourceRecord\" WHERE \"Source\" = {source} AND \"PayloadJson\" = {payload}::jsonb AND \"PayloadHash\" = encode(sha256(convert_to(\"PayloadJson\"::text, 'UTF8')), 'hex')"
            )
            .SingleAsync();
        count.Should().Be(1);
    }

    private static async Task Replay(EquiblesFinancialDbContext db)
    {
        foreach (var operation in new PreserveRetiringEquityIdentitySources().UpOperations)
        {
            operation
                .Should()
                .BeOfType<SqlOperation>(
                    "the model cutover must not drop old workers' cursor columns"
                );
            var sql = (SqlOperation)operation;
            sql.SuppressTransaction.Should()
                .BeTrue(
                    "the idempotent setup commits independently of migration-history insertion"
                );
            await db.Database.ExecuteSqlRawAsync(sql.Sql);
        }
    }

    private static async Task AssertAudit(EquiblesFinancialDbContext db)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Equibles.sln")))
            root = root.Parent;
        var script = await File.ReadAllTextAsync(
            Path.Combine(root.FullName, "scripts", "audit-retiring-equity-identity-sources.sql")
        );
        await db.Database.ExecuteSqlRawAsync(script);
    }
}
